using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Desktop.Agents;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using EsilvaSoft.SlopStudio.Infrastructure;
using EsilvaSoft.SlopStudio.Infrastructure.Agents;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.Anthropic;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.OpenAi;
using EsilvaSoft.SlopStudio.Infrastructure.LocalAi;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.SlopStudio.Desktop;

public partial class App : Avalonia.Application
{
    /// <summary>
    /// Opaque OS-vault slot of the user's Claude API key (P7-L06-HOST). Like the OpenAI default slot, it contains no
    /// credential material; per-account references persisted by the LiteDB owner remain a pending contract.
    /// </summary>
    public static SecretReference ClaudeApiKeySlot { get; } = new(Guid.ParseExact("c3a1d0e6b7f24f0e9a5c8d21e4b7f613", "N"));

    private ServiceProvider? _serviceProvider;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }
        var services = new ServiceCollection();
        services.AddSlopStudioInfrastructure(LocalWorkspacePaths.GetDatabasePath());
        services.AddSlopStudioLocalAiInfrastructure();
        // The Claude (assinatura) mode reads the Files panel folder only when a session is created (fixed per session).
        AddDesktopAgentServices(services, () => _serviceProvider?.GetService<WorkspaceViewModel>()?.WorkspaceRootPath);
        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<WorkspaceViewModel>();
        _serviceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<WorkspaceViewModel>()
            };
            desktop.Exit += (_, _) => _serviceProvider.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Agent platform of the Desktop composition root, after <c>AddSlopStudioInfrastructure</c> (which already composed
    /// <see cref="IAgentRuntime"/> and the shared <see cref="IAgentToolRegistry"/>, closed at exposure stage None).
    /// Public so composition tests exercise exactly this code. Nothing here opens a connection, reads the vault or
    /// awaits: without a stored API Key, network or reachable service each provider only reports itself unavailable
    /// with a safe code through the same runtime/catalog (AC-15).
    /// </summary>
    public static void AddDesktopAgentServices(IServiceCollection services, Func<string?>? workspaceDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSlopStudioOpenAiAgentProvider();
        services.AddSlopStudioClaudeAgentProvider(new ClaudeAgentProviderOptions { ApiKeyReference = ClaudeApiKeySlot });
        // "Claude (assinatura)": the user's own Claude Code binary (ADR-053), a separate provider from the API mode above.
        // Lazy: nothing is located, started or authenticated until the user checks the status or opens a session.
        services.AddSlopStudioClaudeCodeAgentProvider(new ClaudeCodeAgentProviderOptions { WorkspaceDirectory = workspaceDirectory });
        // Production, provider-neutral view for the chat UI (AC-04/AC-09): built only from the shared
        // AgentProviderCatalog/capabilities, with no branch by provider brand.
        services.AddSingleton<IAgentProviderCatalog>(
            provider => new DesktopAgentProviderCatalog(provider.GetRequiredService<AgentProviderCatalog>()));
        // The only write path for provider keys: the same vault slots the adapters resolve. The slot map is the
        // composition root's data; the store itself never branches on a provider brand.
        services.AddSingleton<IAgentApiKeyStore>(provider => new DesktopAgentApiKeyStore(
            provider.GetRequiredService<ISecretStore>(),
            new Dictionary<string, SecretReference>(StringComparer.Ordinal)
            {
                [OpenAiAgentProvider.Id] = OpenAiAgentProviderOptions.DefaultCredentialReference,
                [ClaudeAgentProvider.Id] = ClaudeApiKeySlot,
            }));
        // Resolved only when the user first opens the AI Agent panel. Approval details come from the write approval
        // coordinator composed by the infrastructure (the trusted source of pending registry proposals); no write tool
        // is exposed yet, so in practice no approval is ever requested until lote 10 releases a write source.
        services.AddSingleton(provider => new AgentChatServicesFactory(() => new AgentChatServices(
            provider.GetRequiredService<IAgentRuntime>(),
            provider.GetRequiredService<IAgentProviderCatalog>(),
            provider.GetRequiredService<IAgentContextProvider>(),
            ApprovalDetails: provider.GetRequiredService<IAgentApprovalDetailsSource>(),
            Credentials: provider.GetRequiredService<IAgentApiKeyStore>())));
    }
}
