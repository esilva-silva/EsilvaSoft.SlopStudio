using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Desktop.Agents;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using EsilvaSoft.SlopStudio.Infrastructure;
using EsilvaSoft.SlopStudio.Infrastructure.Agents;
using EsilvaSoft.SlopStudio.Infrastructure.LocalAi;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.SlopStudio.Desktop;

public partial class App : Avalonia.Application
{
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
        // Agent platform (P7-L06-WIRING): AddSlopStudioInfrastructure already composed IAgentRuntime and the shared
        // IAgentToolRegistry closed (exposure stage None) — registering the external adapters here only adds them to
        // the same IAgentProvider set; neither call opens a connection, reads the vault or awaits anything. Without a
        // stored API Key, without network or with the SDK unable to reach the service, each provider only reports
        // itself unavailable with a safe code (AgentProviderStatus.UnavailableCode) through the same runtime/catalog
        // consumed below — it never throws during composition or startup (AC-15).
        services.AddSlopStudioOpenAiAgentProvider();
        services.AddSlopStudioClaudeAgentProvider();
        // Production, provider-neutral view for the chat UI (AC-04/AC-09): built only from the shared
        // AgentProviderCatalog/capabilities, with no branch by provider brand. Not consumed yet: the chat panel is
        // not hosted in the main window in this round (see AgentChatPorts.cs).
        services.AddSingleton<IAgentProviderCatalog>(
            provider => new DesktopAgentProviderCatalog(provider.GetRequiredService<AgentProviderCatalog>()));
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

}
