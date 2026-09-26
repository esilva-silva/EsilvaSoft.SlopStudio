using System.ComponentModel;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;

/// <summary>
/// Modo "Claude (assinatura)": conversa com o Claude pelo binário oficial do Claude Code instalado pelo usuário,
/// executado como subprocesso (ADR-053). A autenticação é 100% do processo oficial: o app não lê <c>~/.claude</c>,
/// arquivos de credencial nem tokens, não usa <c>--bare</c> e nunca cai silenciosamente para API Key — qualquer método
/// efetivo que não seja a assinatura bloqueia o envio. Ferramentas nativas: somente Read/Glob/Grep (ADR-054 revisada);
/// sem tools do produto nem ferramenta de aprovação nesta etapa. Separado do provider "Claude (Anthropic API)".
/// </summary>
public sealed class ClaudeCodeAgentProvider : IAgentProvider
{
    public const string Id = "claude-code";

    public const string DisplayName = "Claude (assinatura)";

    private readonly Func<ClaudeCodeAgentProviderOptions> _options;
    private readonly Func<ClaudeCodeExecutableLocator> _locator;
    private readonly Lock _gate = new();
    private InstallationCacheEntry? _installation;

    public ClaudeCodeAgentProvider(ClaudeCodeAgentProviderOptions options)
        : this(() => options, ClaudeCodeExecutableLocator.ForCurrentProcess)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
    }

    internal ClaudeCodeAgentProvider(Func<ClaudeCodeAgentProviderOptions> options, Func<ClaudeCodeExecutableLocator> locator)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(locator);
        _options = options;
        _locator = locator;
    }

    public string ProviderId => Id;

    /// <summary>Dados saem da máquina (Anthropic), portanto nunca local.</summary>
    public bool IsLocal => false;

    /// <summary>
    /// Capacidades implementadas, com evidência de contrato automatizado (CLI falso com fixtures do spike); a conta real
    /// só é comprovada pela homologação manual (GCL-8). <see cref="AgentProviderCapabilities.ToolCalling"/> é falso:
    /// as leituras nativas da CLI não passam pelo registry e não há tools do produto nesta etapa.
    /// </summary>
    internal static AgentProviderCapabilities ImplementedCapabilities { get; } = new()
    {
        Chat = true,
        Streaming = true,
        ToolCalling = false,
        Sessions = true,
        ModelSelection = true,
        UsesNetwork = true,
        Evidence = AgentCapabilityEvidence.AutomatedContract,
    };

    /// <summary>Descrição estática, sem processo, arquivo ou rede.</summary>
    public AgentProviderDescriptor Describe() =>
        new(Id, DisplayName, [AgentAuthenticationMethod.OfficialCliDelegated], ImplementedCapabilities);

    /// <summary>
    /// Estado sob demanda (a listagem do catálogo não chama este método): executa <c>claude --version</c> (em cache
    /// por arquivo) e <c>claude auth status</c>, ambos locais, curtos e sem envio de dados do usuário.
    /// </summary>
    public async Task<AgentProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        ClaudeCodeAgentProviderOptions options;
        try
        {
            options = SnapshotOptions();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Unavailable(ClaudeCodeUnavailableReason.InvalidConfiguration, AgentProviderAuthState.Unknown);
        }

        var availability = await CheckAvailabilityAsync(options, null, cancellationToken).ConfigureAwait(false);
        if (availability.Reason != ClaudeCodeUnavailableReason.None)
        {
            return Unavailable(availability.Reason, AuthStateOf(availability.Reason));
        }

        return new AgentProviderStatus(true, AgentProviderAuthState.Configured, ImplementedCapabilities with
        {
            ModelSelection = options.AllowedModelIds.Count > 1,
        }, options.AllowedModelIds, options.DefaultModel);
    }

    /// <summary>Detecção do executável nativo e da versão, sem autenticação.</summary>
    public Task<ClaudeCodeInstallation> DetectAsync(CancellationToken cancellationToken = default) =>
        DetectAsync(SnapshotOptions(), cancellationToken);

    /// <summary>Estado de autenticação atual, com o mesmo argv global/env/cwd de um turno.</summary>
    public async Task<ClaudeCodeAuthStatus> GetAuthenticationStatusAsync(CancellationToken cancellationToken = default)
    {
        var options = SnapshotOptions();
        var installation = await DetectAsync(options, cancellationToken).ConfigureAwait(false);
        if (!installation.IsUsable)
        {
            return new ClaudeCodeAuthStatus(ClaudeCodeAuthKind.Unreadable);
        }

        var profile = CreateProfile(options, installation, options.DefaultModel ?? options.AllowedModelIds[0]);
        return await QueryAuthStatusAsync(options, profile, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Abre <c>claude auth login</c> numa janela visível; o login termina no navegador com a Anthropic. O app não lê a
    /// saída do processo e só reconsulta <c>auth status</c> depois que a janela fecha (ou o prazo acaba).
    /// </summary>
    public Task<ClaudeCodeAccountCommandResult> LoginAsync(CancellationToken cancellationToken = default) =>
        RunAccountCommandAsync(ClaudeCodeCommandLine.LoginArguments, cancellationToken);

    /// <summary>
    /// Abre <c>claude auth logout</c> numa janela visível. O logout é global (afeta o Claude Code do usuário fora do
    /// app), por isso exige confirmação explícita já obtida pela UI.
    /// </summary>
    public Task<ClaudeCodeAccountCommandResult> LogoutAsync(bool userConfirmedGlobalLogout, CancellationToken cancellationToken = default)
    {
        if (!userConfirmedGlobalLogout)
        {
            throw new InvalidOperationException("O logout do Claude Code é global e exige confirmação do usuário.");
        }

        return RunAccountCommandAsync(ClaudeCodeCommandLine.LogoutArguments, cancellationToken);
    }

    public async Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!string.Equals(options.ProviderId, Id, StringComparison.Ordinal))
        {
            throw new ArgumentException("A sessão não pertence ao provider Claude (assinatura).", nameof(options));
        }

        // Snapshot antes de qualquer await: configuração, modelo e workspace ficam fixos para esta sessão.
        ClaudeCodeAgentProviderOptions configuration;
        try
        {
            configuration = SnapshotOptions();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new ClaudeCodeUnavailableException(ClaudeCodeUnavailableReason.InvalidConfiguration);
        }

        if (!TrySelectModel(configuration, options.ModelId, out var model, out var reason))
        {
            throw new ClaudeCodeUnavailableException(reason);
        }

        var availability = await CheckAvailabilityAsync(configuration, model, cancellationToken).ConfigureAwait(false);
        if (availability.Reason != ClaudeCodeUnavailableReason.None)
        {
            throw new ClaudeCodeUnavailableException(availability.Reason);
        }

        return new ClaudeCodeAgentSession(this, configuration, availability.Profile!);
    }

    /// <summary>Verificação imediatamente antes de escrever o prompt: ambiente, executável e método efetivo.</summary>
    internal async Task<string?> CheckTurnPreconditionsAsync(ClaudeCodeLaunchProfile profile, CancellationToken cancellationToken)
    {
        var options = SnapshotOptions();
        if (ClaudeCodeAuthStatus.FindBlockingEnvironmentVariable(options.IsEnvironmentVariableSet) is not null)
        {
            return ClaudeCodeErrorCodes.BlockedEnvironment;
        }

        if (ClaudeCodeExecutableLocator.Validate(profile.ExecutablePath) is null)
        {
            return ClaudeCodeErrorCodes.ExecutableUnavailable;
        }

        var status = await QueryAuthStatusAsync(options, profile, cancellationToken).ConfigureAwait(false);
        return status.Kind switch
        {
            ClaudeCodeAuthKind.Subscription => null,
            ClaudeCodeAuthKind.NotLoggedIn => ClaudeCodeErrorCodes.NotLoggedIn,
            ClaudeCodeAuthKind.BlockedEnvironment => ClaudeCodeErrorCodes.BlockedEnvironment,
            ClaudeCodeAuthKind.Unreadable => ClaudeCodeErrorCodes.AuthStatusUnavailable,
            _ => ClaudeCodeErrorCodes.NonSubscriptionAuthentication,
        };
    }

    private sealed record Availability(ClaudeCodeUnavailableReason Reason, ClaudeCodeLaunchProfile? Profile = null);

    private async Task<Availability> CheckAvailabilityAsync(
        ClaudeCodeAgentProviderOptions options, string? model, CancellationToken cancellationToken)
    {
        if (!TrySelectModel(options, model, out var selected, out var modelReason))
        {
            return new(modelReason);
        }

        if (ClaudeCodeAuthStatus.FindBlockingEnvironmentVariable(options.IsEnvironmentVariableSet) is not null)
        {
            return new(ClaudeCodeUnavailableReason.BlockedEnvironment);
        }

        var installation = await DetectAsync(options, cancellationToken).ConfigureAwait(false);
        if (!installation.IsUsable)
        {
            return new(installation.State switch
            {
                ClaudeCodeInstallationState.NotFound => ClaudeCodeUnavailableReason.ExecutableNotFound,
                ClaudeCodeInstallationState.UnsupportedExecutable => ClaudeCodeUnavailableReason.UnsupportedExecutable,
                ClaudeCodeInstallationState.VersionTooLow => ClaudeCodeUnavailableReason.VersionTooLow,
                ClaudeCodeInstallationState.VersionUnreadable => ClaudeCodeUnavailableReason.VersionUnreadable,
                ClaudeCodeInstallationState.ProbeTimedOut => ClaudeCodeUnavailableReason.ProbeTimedOut,
                _ => ClaudeCodeUnavailableReason.ProbeFailed,
            });
        }

        ClaudeCodeLaunchProfile profile;
        try
        {
            profile = CreateProfile(options, installation, selected);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(ClaudeCodeUnavailableReason.InvalidConfiguration);
        }

        var status = await QueryAuthStatusAsync(options, profile, cancellationToken).ConfigureAwait(false);
        return status.Kind switch
        {
            ClaudeCodeAuthKind.Subscription => new(ClaudeCodeUnavailableReason.None, profile),
            ClaudeCodeAuthKind.NotLoggedIn => new(ClaudeCodeUnavailableReason.NotLoggedIn),
            ClaudeCodeAuthKind.BlockedEnvironment => new(ClaudeCodeUnavailableReason.BlockedEnvironment),
            ClaudeCodeAuthKind.Unreadable => new(ClaudeCodeUnavailableReason.AuthStatusUnreadable),
            _ => new(ClaudeCodeUnavailableReason.NonSubscriptionAuthentication),
        };
    }

    private async Task<ClaudeCodeInstallation> DetectAsync(ClaudeCodeAgentProviderOptions options, CancellationToken cancellationToken)
    {
        var (path, state) = _locator().Locate(options.ExecutablePath);
        if (path is null)
        {
            return new ClaudeCodeInstallation(state);
        }

        FileInfo file;
        try
        {
            file = new FileInfo(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new ClaudeCodeInstallation(ClaudeCodeInstallationState.NotFound);
        }

        var key = new InstallationKey(path, file.LastWriteTimeUtc, file.Length, options.MinimumVersion);
        lock (_gate)
        {
            if (_installation is { } cached && cached.Key == key)
            {
                return cached.Installation;
            }
        }

        string workingDirectory;
        try
        {
            workingDirectory = ClaudeCodeWorkspacePolicy.Normalize(options.ResolveDedicatedWorkingDirectory());
            Directory.CreateDirectory(workingDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new ClaudeCodeInstallation(ClaudeCodeInstallationState.ProbeFailed, path);
        }

        ClaudeCodeProbeResult probe;
        try
        {
            probe = await ClaudeCodeProbe.RunAsync(path, ClaudeCodeCommandLine.VersionArguments, workingDirectory, options.ProbeTimeout,
                1024, options.MaxStderrBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException)
        {
            return new ClaudeCodeInstallation(ClaudeCodeInstallationState.ProbeFailed, path);
        }

        ClaudeCodeInstallation installation;
        if (probe.TimedOut)
        {
            // Não fica em cache: pode ter sido lentidão momentânea.
            return new ClaudeCodeInstallation(ClaudeCodeInstallationState.ProbeTimedOut, path);
        }

        if (probe.Overflow || probe.ExitCode != 0 || !ClaudeCodeVersion.TryParse(probe.Output, out var version))
        {
            installation = new ClaudeCodeInstallation(ClaudeCodeInstallationState.VersionUnreadable, path);
        }
        else
        {
            installation = version < options.MinimumVersion
                ? new ClaudeCodeInstallation(ClaudeCodeInstallationState.VersionTooLow, path, version)
                : new ClaudeCodeInstallation(ClaudeCodeInstallationState.Found, path, version);
        }

        lock (_gate)
        {
            _installation = new InstallationCacheEntry(key, installation);
        }

        return installation;
    }

    private static async Task<ClaudeCodeAuthStatus> QueryAuthStatusAsync(
        ClaudeCodeAgentProviderOptions options, ClaudeCodeLaunchProfile profile, CancellationToken cancellationToken)
    {
        if (ClaudeCodeAuthStatus.FindBlockingEnvironmentVariable(options.IsEnvironmentVariableSet) is { } variable)
        {
            return new ClaudeCodeAuthStatus(ClaudeCodeAuthKind.BlockedEnvironment, EnvironmentVariableName: variable);
        }

        ClaudeCodeProbeResult probe;
        try
        {
            probe = await ClaudeCodeProbe.RunAsync(profile.ExecutablePath, ClaudeCodeCommandLine.AuthStatusArguments(profile),
                profile.WorkingDirectory, options.ProbeTimeout, options.MaxProbeOutputBytes, options.MaxStderrBytes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException)
        {
            return new ClaudeCodeAuthStatus(ClaudeCodeAuthKind.Unreadable);
        }

        // Exit 1 com loggedIn=false é "não autenticado"; qualquer saída é filtrada pela allowlist de campos.
        return probe.TimedOut || probe.Overflow ? new ClaudeCodeAuthStatus(ClaudeCodeAuthKind.Unreadable) : ClaudeCodeAuthStatus.Parse(probe.Output);
    }

    private async Task<ClaudeCodeAccountCommandResult> RunAccountCommandAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var options = SnapshotOptions();
        var installation = await DetectAsync(options, cancellationToken).ConfigureAwait(false);
        if (!installation.IsUsable)
        {
            return new ClaudeCodeAccountCommandResult(ClaudeCodeAccountCommandState.ExecutableUnavailable, null);
        }

        var profile = CreateProfile(options, installation, options.DefaultModel ?? options.AllowedModelIds[0]);
        var state = await ClaudeCodeAccountCommands.RunVisibleAsync(installation.ExecutablePath!, arguments, profile.WorkingDirectory,
            options.AccountCommandTimeout, cancellationToken).ConfigureAwait(false);
        var status = await QueryAuthStatusAsync(options, profile, cancellationToken).ConfigureAwait(false);
        return new ClaudeCodeAccountCommandResult(state, status);
    }

    private static ClaudeCodeLaunchProfile CreateProfile(ClaudeCodeAgentProviderOptions options, ClaudeCodeInstallation installation, string model)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var (directory, kind) = ClaudeCodeWorkspacePolicy.Resolve(options, home);
        var deny = ClaudeCodeCommandLine.BuildDenyRules(options.ResolveAppDataDirectory(), options.ResolveDatabasePath(), home);
        return new ClaudeCodeLaunchProfile(installation.ExecutablePath!, installation.Version!.Value, directory, kind,
            ClaudeCodeCommandLine.BuildSettingsJson(kind, deny), model);
    }

    private ClaudeCodeAgentProviderOptions SnapshotOptions()
    {
        var options = _options() ?? throw new InvalidOperationException("Configuração do Claude (assinatura) ausente.");
        options.Validate();
        return options;
    }

    private static bool TrySelectModel(
        ClaudeCodeAgentProviderOptions options, string? requested, out string model, out ClaudeCodeUnavailableReason reason)
    {
        model = string.Empty;
        var candidate = string.IsNullOrEmpty(requested) ? options.DefaultModel : requested;
        if (candidate is null)
        {
            reason = ClaudeCodeUnavailableReason.NoModelSelected;
            return false;
        }

        if (!options.AllowedModelIds.Contains(candidate, StringComparer.Ordinal))
        {
            reason = ClaudeCodeUnavailableReason.ModelNotAllowed;
            return false;
        }

        model = candidate;
        reason = ClaudeCodeUnavailableReason.None;
        return true;
    }

    private static AgentProviderAuthState AuthStateOf(ClaudeCodeUnavailableReason reason) => reason switch
    {
        ClaudeCodeUnavailableReason.NotLoggedIn => AgentProviderAuthState.NotConfigured,
        ClaudeCodeUnavailableReason.NonSubscriptionAuthentication or ClaudeCodeUnavailableReason.BlockedEnvironment =>
            AgentProviderAuthState.Invalid,
        _ => AgentProviderAuthState.Unknown,
    };

    // Indisponível não declara capacidades, só o fato restritivo de que dados sairiam da máquina.
    private static AgentProviderStatus Unavailable(ClaudeCodeUnavailableReason reason, AgentProviderAuthState authState) =>
        new(false, authState, AgentProviderCapabilities.None with { UsesNetwork = true }, unavailableCode: reason.ToString());

    private readonly record struct InstallationKey(string Path, DateTime LastWriteUtc, long Length, ClaudeCodeVersion MinimumVersion);

    private sealed record InstallationCacheEntry(InstallationKey Key, ClaudeCodeInstallation Installation);
}
