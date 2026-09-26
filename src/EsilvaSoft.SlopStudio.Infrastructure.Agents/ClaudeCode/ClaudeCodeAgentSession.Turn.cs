using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;

internal sealed partial class ClaudeCodeAgentSession
{
    private const string NoConversationMarker = "No conversation found";

    private async Task ProduceTurnAsync(
        TurnContext turn, string userMessage, string? authorizedContext, ChannelWriter<AgentProviderEvent> writer)
    {
        var translator = new ClaudeCodeStreamTranslator(turn.CliSessionId, _options.MinimumVersion, _profile.Model);
        string? error = null;
        var resultReceived = false;
        var discarded = 0;
        // Prazo esgotado também encerra a árvore; o cancelamento do usuário já a encerra em TurnContext.Cancel.
        using var killOnDeadline = turn.WorkToken.Register(static state => ((TurnContext)state!).KillProcessTree(), turn);
        try
        {
            error = Validate(userMessage, authorizedContext);
            if (error is null)
            {
                // Bloqueio preventivo: o init só chega depois da primeira mensagem (H-21), então o método efetivo é
                // verificado com o mesmo binário, env, cwd e flags globais antes de qualquer escrita no stdin.
                error = await _provider.CheckTurnPreconditionsAsync(_profile, turn.WorkToken).ConfigureAwait(false);
            }

            if (error is null)
            {
                (error, resultReceived, discarded) = await RunProcessAsync(turn, translator, userMessage, authorizedContext, writer)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (turn.UserToken.IsCancellationRequested || turn.WorkToken.IsCancellationRequested)
        {
            // Tratado abaixo pela precedência cancelamento > prazo > erro.
        }
        catch (Exception)
        {
            error = ClaudeCodeErrorCodes.ProviderFailure;
        }
        finally
        {
            AgentTurnOutcome outcome;
            if (resultReceived)
            {
                outcome = error is null ? AgentTurnOutcome.Completed : AgentTurnOutcome.Failed;
            }
            else if (turn.UserToken.IsCancellationRequested)
            {
                // Árvore encerrada pelo cancelamento: o que a CLI já executou não é desfeito nem confirmado. Antes de o
                // prompt sair, nada foi enviado e o cancelamento é limpo.
                outcome = turn.PromptSent ? AgentTurnOutcome.OutcomeUnknown : AgentTurnOutcome.Cancelled;
                error = null;
            }
            else if (turn.WorkToken.IsCancellationRequested)
            {
                outcome = AgentTurnOutcome.TimedOut;
                error = ClaudeCodeErrorCodes.TurnTimeout;
            }
            else
            {
                outcome = error is null ? AgentTurnOutcome.Completed : AgentTurnOutcome.Failed;
            }

            if (error is not null)
            {
                await EmitErrorAsync(writer, turn, error).ConfigureAwait(false);
            }

            var result = translator.Result;
            CompleteTurn(turn, new ClaudeCodeTurnSummary(
                    outcome, error, turn.Resume, result?.TotalCostUsd, result?.InputTokens, result?.OutputTokens, result?.NumTurns,
                    result?.PermissionDenials ?? 0, result?.TerminalReason, translator.ApiRetries, translator.LastApiRetryCategory,
                    translator.NativeToolCalls, discarded, translator.ObservedModel),
                established: translator.InitValidated,
                resetSession: error == ClaudeCodeErrorCodes.SessionNotFound);
            writer.TryComplete();
        }
    }

    private string? Validate(string userMessage, string? authorizedContext)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return ClaudeCodeErrorCodes.EmptyMessage;
        }

        return userMessage.Length + (authorizedContext?.Length ?? 0) > _options.MaxUserInputChars ? ClaudeCodeErrorCodes.InputTooLarge : null;
    }

    private async Task<(string? Error, bool ResultReceived, int Discarded)> RunProcessAsync(
        TurnContext turn, ClaudeCodeStreamTranslator translator, string userMessage, string? authorizedContext,
        ChannelWriter<AgentProviderEvent> writer)
    {
        var arguments = ClaudeCodeCommandLine.TurnArguments(_profile, _options.MaxTurns, turn.CliSessionId, turn.Resume);
        ClaudeCodeProcess process;
        try
        {
            process = ClaudeCodeProcess.Start(_profile.ExecutablePath, arguments, _profile.WorkingDirectory, _options.MaxStderrBytes);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException)
        {
            return (ClaudeCodeErrorCodes.StartFailed, false, 0);
        }

        await using (process.ConfigureAwait(false))
        {
            // Associado antes de escrever: um cancelamento concorrente encerra a árvore sem esperar o stdin.
            turn.Attach(process);
            turn.WorkToken.ThrowIfCancellationRequested();
            turn.PromptSent = true;
            try
            {
                await process.StandardInput.WriteAsync(BuildUserMessageLine(userMessage, authorizedContext).AsMemory(), turn.WorkToken)
                    .ConfigureAwait(false);
                await process.StandardInput.FlushAsync(turn.WorkToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // A CLI saiu antes de ler a mensagem; o stdout diz o motivo (ou o fim prematuro vira erro).
            }

            var reader = new ClaudeCodeLineReader(process.StandardOutput, _options.MaxLineBytes, _options.MaxTurnOutputBytes);
            var events = new List<AgentProviderEvent>(8);
            var discarded = 0;
            while (true)
            {
                var read = await reader.ReadAsync(turn.WorkToken).ConfigureAwait(false);
                events.Clear();
                switch (read.Kind)
                {
                    case LineReadKind.Line:
                        var step = translator.Translate(read.Text!, events);
                        await PublishAsync(writer, turn, events).ConfigureAwait(false);
                        switch (step.Kind)
                        {
                            case TranslationKind.Invalid when ++discarded > _options.MaxDiscardedLines:
                                process.KillTree();
                                return (ClaudeCodeErrorCodes.ProtocolViolation, false, discarded);
                            case TranslationKind.Abort:
                                process.KillTree();
                                return (step.ErrorCode, false, discarded);
                            case TranslationKind.Result:
                                await FinishProcessAsync(process).ConfigureAwait(false);
                                var resultError = translator.ResultErrorCode();
                                if (resultError == ClaudeCodeErrorCodes.ExecutionError && StderrSaysSessionMissing(process))
                                {
                                    resultError = ClaudeCodeErrorCodes.SessionNotFound;
                                }

                                return (resultError, true, discarded);
                        }

                        break;
                    case LineReadKind.Oversized or LineReadKind.InvalidEncoding:
                        if (++discarded > _options.MaxDiscardedLines)
                        {
                            process.KillTree();
                            return (ClaudeCodeErrorCodes.ProtocolViolation, false, discarded);
                        }

                        break;
                    case LineReadKind.TotalLimitExceeded:
                        process.KillTree();
                        return (ClaudeCodeErrorCodes.OutputLimitExceeded, false, discarded);
                    default:
                        // Fim do stdout sem result: processo encerrado (crash, kill) ou saída truncada.
                        translator.CloseOpenMessages(events);
                        await PublishAsync(writer, turn, events).ConfigureAwait(false);
                        turn.WorkToken.ThrowIfCancellationRequested();
                        var exited = await process.WaitForExitAsync(_options.ExitTimeout).ConfigureAwait(false);
                        if (!exited)
                        {
                            process.KillTree();
                        }

                        turn.WorkToken.ThrowIfCancellationRequested();
                        return (StderrSaysSessionMissing(process) ? ClaudeCodeErrorCodes.SessionNotFound
                            : exited && process.ExitCode is not 0 ? ClaudeCodeErrorCodes.ProcessFailed
                            : ClaudeCodeErrorCodes.StreamIncomplete, false, discarded);
                }
            }
        }
    }

    /// <summary>Depois do result: fecha o stdin e espera o término; se a CLI não sair, encerra a árvore.</summary>
    private async Task FinishProcessAsync(ClaudeCodeProcess process)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
        }

        if (!await process.WaitForExitAsync(_options.ExitTimeout).ConfigureAwait(false))
        {
            process.KillTree();
        }
    }

    // O stderr só é consultado por um marcador fixo; seu conteúdo nunca vai para eventos, logs ou exceções.
    private static bool StderrSaysSessionMissing(ClaudeCodeProcess process) =>
        process.StderrSnapshot.Contains(NoConversationMarker, StringComparison.Ordinal);

    private static async Task PublishAsync(ChannelWriter<AgentProviderEvent> writer, TurnContext turn, List<AgentProviderEvent> events)
    {
        foreach (var item in events)
        {
            await writer.WriteAsync(item, turn.UserToken).ConfigureAwait(false);
        }
    }

    /// <summary>Mensagem stream-json de entrada; o contexto autorizado da aba vai como bloco delimitado de dados.</summary>
    internal static string BuildUserMessageLine(string userMessage, string? authorizedContext)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("type", "user");
            json.WriteStartObject("message");
            json.WriteString("role", "user");
            json.WriteStartArray("content");
            WriteText(json, userMessage);
            if (!string.IsNullOrEmpty(authorizedContext))
            {
                WriteText(json, "<contexto_autorizado>\n" + authorizedContext + "\n</contexto_autorizado>");
            }

            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";

        static void WriteText(Utf8JsonWriter json, string text)
        {
            json.WriteStartObject();
            json.WriteString("type", "text");
            json.WriteString("text", text);
            json.WriteEndObject();
        }
    }

    private static async Task EmitErrorAsync(ChannelWriter<AgentProviderEvent> writer, TurnContext turn, string code)
    {
        if (turn.UserToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await writer.WriteAsync(new AgentProviderEvent(AgentEventKind.AgentError, code), turn.UserToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ChannelClosedException)
        {
        }
    }
}
