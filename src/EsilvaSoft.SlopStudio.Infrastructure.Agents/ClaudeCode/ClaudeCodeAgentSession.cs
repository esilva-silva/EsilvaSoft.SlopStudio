using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;

/// <summary>
/// Estado final de um turno do Claude Code visto pelo adapter. <see cref="AgentTurnOutcome.OutcomeUnknown"/> quando a
/// árvore de processos foi encerrada (cancelamento): o que a CLI já fez não é desfeito nem confirmado.
/// </summary>
internal sealed record ClaudeCodeTurnSummary(
    AgentTurnOutcome Outcome,
    string? ErrorCode,
    bool Resumed,
    double? TotalCostUsd = null,
    long? InputTokens = null,
    long? OutputTokens = null,
    int? NumTurns = null,
    int PermissionDenials = 0,
    string? TerminalReason = null,
    int ApiRetries = 0,
    string? LastApiRetryCategory = null,
    int NativeToolCalls = 0,
    int DiscardedLines = 0,
    string? ObservedModel = null);

/// <summary>
/// Sessão do modo Claude (assinatura): um processo <c>claude -p</c> por turno, continuidade por <c>--resume</c> com o
/// <c>session_id</c> guardado só em memória, um turno ativo por vez e cancelamento exclusivamente por encerramento da
/// árvore de processos (decisão do usuário, 25/09/2026; o <c>control_request</c> interno não é usado). O produtor
/// escreve numa fila limitada; tipos e textos nativos não saem do adapter.
/// </summary>
internal sealed partial class ClaudeCodeAgentSession : IAgentSession
{
    private const int EventQueueCapacity = 64;

    private readonly ClaudeCodeAgentProvider _provider;
    private readonly ClaudeCodeAgentProviderOptions _options;
    private readonly ClaudeCodeLaunchProfile _profile;
    private readonly Lock _gate = new();
    private string _cliSessionId = Guid.NewGuid().ToString("D");
    private bool _established;
    private TurnContext? _active;
    private ClaudeCodeTurnSummary? _lastTurn;
    private int _disposed;

    public ClaudeCodeAgentSession(ClaudeCodeAgentProvider provider, ClaudeCodeAgentProviderOptions options, ClaudeCodeLaunchProfile profile)
    {
        _provider = provider;
        _options = options;
        _profile = profile;
    }

    internal ClaudeCodeLaunchProfile Profile => _profile;

    /// <summary>Diagnóstico do último turno (sem texto); nunca persistido.</summary>
    internal ClaudeCodeTurnSummary? LastTurn
    {
        get
        {
            lock (_gate)
            {
                return _lastTurn;
            }
        }
    }

    internal (string SessionId, bool Established) CliSession
    {
        get
        {
            lock (_gate)
            {
                return (_cliSessionId, _established);
            }
        }
    }

    public async IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(
        AgentTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!request.TurnId.IsValid)
        {
            throw new ArgumentException("Turno inválido.", nameof(request));
        }

        // Snapshot antes de qualquer await: texto, contexto e sessão da CLI ficam fixos para o turno.
        var userMessage = request.UserMessage ?? string.Empty;
        var authorizedContext = request.AuthorizedContext;
        var turn = new TurnContext(request.TurnId, _options.MaxTurnDuration);
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                turn.Dispose();
                throw new ObjectDisposedException(GetType().FullName);
            }

            if (_active is not null)
            {
                turn.Dispose();
                throw new InvalidOperationException("A sessão Claude (assinatura) já tem um turno ativo.");
            }

            _active = turn;
            turn.CliSessionId = _cliSessionId;
            turn.Resume = _established;
        }

        var channel = Channel.CreateBounded<AgentProviderEvent>(new BoundedChannelOptions(EventQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        var registration = cancellationToken.Register(static state => ((TurnContext)state!).Cancel(), turn);
        var producer = Task.Run(() => ProduceTurnAsync(turn, userMessage, authorizedContext, channel.Writer), CancellationToken.None);
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            // Consumidor saiu (fim natural, cancelamento ou abandono): encerra a árvore de processos se ainda viva.
            turn.Cancel();
            await registration.DisposeAsync().ConfigureAwait(false);
            try
            {
                await producer.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // O produtor converte falhas em eventos; nada a propagar aqui.
            }

            lock (_gate)
            {
                if (ReferenceEquals(_active, turn))
                {
                    _active = null;
                }
            }

            turn.Dispose();
        }
    }

    // Sem tools do produto nesta etapa (P7-CL4-03 pendente): nenhum pedido de tool é publicado ao runtime.
    public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException("Chamada de tool desconhecida ou já respondida."));

    // A CLI não pede aprovação ao app nesta etapa (sem --permission-prompt-tool; leituras "ask" são negadas pela CLI).
    public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException("O provider Claude (assinatura) não solicita aprovações nesta versão."));

    public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken)
    {
        TurnContext? turn;
        lock (_gate)
        {
            turn = _active;
        }

        // Idempotente e isolado: encerra só a árvore do turno indicado desta sessão; nada é desfeito.
        if (turn is not null && turn.TurnId == turnId)
        {
            turn.Cancel();
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        TurnContext? turn;
        lock (_gate)
        {
            turn = _active;
            // Só o identificador em memória é esquecido; o transcript gravado pela própria CLI em ~/.claude/projects
            // permanece (limitação aceita, plano 23 regra 8) e o app não o lê nem apaga.
            _established = false;
        }

        turn?.Cancel();
        return ValueTask.CompletedTask;
    }

    private void CompleteTurn(TurnContext turn, ClaudeCodeTurnSummary summary, bool? established, bool resetSession)
    {
        lock (_gate)
        {
            _lastTurn = summary;
            if (Volatile.Read(ref _disposed) != 0 || !string.Equals(turn.CliSessionId, _cliSessionId, StringComparison.Ordinal))
            {
                return;
            }

            if (resetSession)
            {
                // Sessão da CLI inexistente/expirada: o próximo turno começa outra, sem transportar contexto.
                _cliSessionId = Guid.NewGuid().ToString("D");
                _established = false;
            }
            else if (established == true)
            {
                _established = true;
            }
        }
    }

    /// <summary>Estado privado do turno: tokens separados distinguem cancelamento de prazo; o processo é encerrado no Cancel.</summary>
    private sealed class TurnContext : IDisposable
    {
        private readonly CancellationTokenSource _user = new();
        private readonly CancellationTokenSource _work;
        private readonly Lock _processGate = new();
        private ClaudeCodeProcess? _process;

        public TurnContext(AgentTurnId turnId, TimeSpan maxDuration)
        {
            TurnId = turnId;
            _work = CancellationTokenSource.CreateLinkedTokenSource(_user.Token);
            _work.CancelAfter(maxDuration);
        }

        public AgentTurnId TurnId { get; }

        public string CliSessionId { get; set; } = string.Empty;

        public bool Resume { get; set; }

        /// <summary>
        /// Verdadeiro a partir do instante em que o prompt começa a ser escrito no stdin: antes disso, cancelar não
        /// enviou nada (Cancelled); depois, o efeito na CLI é desconhecido (OutcomeUnknown).
        /// </summary>
        public bool PromptSent { get; set; }

        public CancellationToken UserToken => _user.Token;

        public CancellationToken WorkToken => _work.Token;

        /// <summary>Associa o processo; se o turno já foi cancelado, a árvore é encerrada imediatamente.</summary>
        public void Attach(ClaudeCodeProcess process)
        {
            lock (_processGate)
            {
                _process = process;
            }

            if (_work.IsCancellationRequested)
            {
                process.KillTree();
            }
        }

        public void KillProcessTree()
        {
            ClaudeCodeProcess? process;
            lock (_processGate)
            {
                process = _process;
            }

            process?.KillTree();
        }

        public void Cancel()
        {
            try
            {
                _user.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            KillProcessTree();
        }

        public void Dispose()
        {
            _work.Dispose();
            _user.Dispose();
        }
    }
}
