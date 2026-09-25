using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Application.SchemaLearning;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.LocalAi.Core;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSlopStudioInfrastructure(this IServiceCollection services, string workspaceDatabasePath)
    {
        services.AddSingleton<IApplicationOperationService, ApplicationOperationService>();
        services.AddSingleton<ICodeFormatter, MongoCodeFormatter>();
        services.AddSingleton<ICodeValidator, MongoCodeValidator>();
        services.AddSingleton<IResultPageExportService, LocalResultPageExportService>();
        services.AddSingleton<MongoClientPool>();
        services.AddSingleton<IAutocompleteDiagnostics, AutocompleteDiagnostics>();
        services.AddSingleton<AiAutocompleteProvider>(provider => new(provider.GetRequiredService<ILocalAiModelService>()));
        services.AddSingleton<IAutocompleteService, AutocompleteService>();
        services.AddSingleton<IAiChatService, LocalModelAiChatService>();
        // Provider local: só cria sessão quando há modelo utilizável; sem rede, conta ou fallback externo.
        services.AddSingleton<LocalAgentProvider>();
        services.AddSingleton<IAgentProvider>(provider => provider.GetRequiredService<LocalAgentProvider>());
        services.AddSingleton<LiteDbConnectionProfileRepository>(provider =>
        {
            var secrets = provider.GetRequiredService<ISecretStore>();
            var owner = new LiteDbConnectionProfileRepository(workspaceDatabasePath, secrets);
            // Lote 1: resume credential journals left by a crash, in the background, on the same owner.
            owner.StartCredentialRecovery(new LegacyConnectionCredentialMigration(owner, secrets));
            return owner;
        });
        services.AddSingleton<IConnectionProfileCredentialStatusProvider>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IConnectionProfileRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IQueryHistoryRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IScriptHistoryRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<ISavedQueryRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IAuditRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IWorkspaceSessionRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IEnvironmentVaultRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<ILegacyCredentialInventoryRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<ILegacyConnectionCredentialMigration, LegacyConnectionCredentialMigration>();
        services.AddSingleton<IConsoleHistoryRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IAgentAuthorizationPolicyProvider>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IAgentAuthorizationPolicyRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IAgentAuditRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        // Single issuer of AgentPrincipal: channel rows live in the same owner, proofs only in ISecretStore.
        services.AddSingleton<IAgentPrincipalAuthority>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IAgentPermissionEvaluator, AgentPermissionEvaluator>();
        // L14: learned schema lives in the same file, owned by the same instance. Never a second LiteDatabase.
        services.AddSingleton<ILearnedSchemaRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IConsoleDatabaseSessionFactory, ConsoleDatabaseSessionFactory>();
        services.AddSingleton<IConsoleRuntime, ConsoleRuntime>();
        services.AddSingleton<IConnectionSecretStore, SessionConnectionSecretStore>();
        services.AddSingleton<ISecretStore>(_ => CreateAgentSecretStore(OperatingSystem.IsWindows(), OperatingSystem.IsLinux()));
        services.AddSingleton<IAgentCredentialProvider, AgentCredentialProvider>();
        services.AddSingleton<IMongoWorkspaceService, MongoWorkspaceService>();
        // Autocomplete knowledge: invalidations from IDE operations, driver metadata and the in-memory catalog.
        services.AddSingleton<IMetadataInvalidationBus, MetadataInvalidationBus>();
        services.AddSingleton<IMongoMetadataSource, MongoMetadataSource>();
        services.AddSingleton<IMetadataCache>(provider => new MetadataCache(provider.GetRequiredService<IMongoMetadataSource>(),
            provider.GetRequiredService<IApplicationOperationService>(), provider.GetRequiredService<IMetadataInvalidationBus>()));
        services.AddSingleton<ICatalogSource>(_ => new LanguageCatalogSource());
        services.AddSingleton<ICatalogSource>(provider => new MetadataCatalogSource(provider.GetRequiredService<IMetadataCache>()));
        // L15: registered right after MetadataCatalogSource and never before it. KnowledgeCatalog consumes
        // IEnumerable<ICatalogSource> in registration order, and DEC-L15-DEDUP folds the learned annotation into the
        // live-evidence symbol already in the sink — which only happens if live evidence was collected first.
        services.AddSingleton<LearnedSchemaOptOut>();
        services.AddSingleton<ILearnedSchemaOptOut>(provider => provider.GetRequiredService<LearnedSchemaOptOut>());
        // Registered as its own concrete type — not only as ICatalogSource — so WorkspaceService can also take it as
        // an optional constructor parameter and call InvalidateProfile on deletion; both resolve the same singleton.
        services.AddSingleton<LearnedSchemaCatalogSource>(provider => new LearnedSchemaCatalogSource(
            provider.GetRequiredService<ILearnedSchemaRepository>(), provider.GetRequiredService<IMetadataCache>(),
            provider.GetRequiredService<ILearnedSchemaOptOut>()));
        services.AddSingleton<ICatalogSource>(provider => provider.GetRequiredService<LearnedSchemaCatalogSource>());
        services.AddSingleton<IKnowledgeCatalog, KnowledgeCatalog>();
        // L15: producer side, one queue and one worker for the whole application. The coordinator is owned by
        // SchemaLearningHost instead of being a service of its own — see that type for why (it is IAsyncDisposable
        // only, and the desktop disposes this container synchronously at Exit). BackgroundSchemaAnalyzer's optional
        // constructor parameters are tuning knobs, not services, so it is built explicitly with its defaults.
        services.AddSingleton<BackgroundSchemaAnalyzer>(_ => new BackgroundSchemaAnalyzer(TimeProvider.System));
        services.AddSingleton<SchemaLearningHost>(provider => new SchemaLearningHost(
            provider.GetRequiredService<BackgroundSchemaAnalyzer>(), provider.GetRequiredService<ILearnedSchemaRepository>(),
            provider.GetRequiredService<LearnedSchemaCatalogSource>()));
        services.AddSingleton<SchemaLearningService>(provider => provider.GetRequiredService<SchemaLearningHost>().Service);
        services.AddSingleton<IExplorerMetadataService, ExplorerMetadataService>();
        services.AddSingleton<IScriptExecutionService, MongoshScriptExecutionService>();
        services.AddSingleton<IScriptFileService, LocalScriptFileService>();
        services.AddSingleton<ITextFileService>(services => (ITextFileService)services.GetRequiredService<IScriptFileService>());
        services.AddSingleton<IWorkspaceFileService, LocalWorkspaceFileService>();
        services.AddSingleton<IAppUpdateService>(_ => new GitHubAppUpdateService(AppUpdateOptions.FromProcess()));
        return services;
    }

    /// <summary>
    /// Opt-in composition of the local MCP broker (lote 3). Without <see cref="AgentBrokerOptions.Enabled"/> and an
    /// explicit stage nothing is registered, so the IDE keeps working without MCP. When enabled, the broker and every
    /// future ingress resolve the same <see cref="IAgentToolRegistry"/> singleton, backed by the single LiteDB owner
    /// facets already registered by <see cref="AddSlopStudioInfrastructure"/>. Only the read-only stages up to
    /// <see cref="AgentToolExposureStage.LiteralQueries"/> can be released here.
    /// </summary>
    public static IServiceCollection AddSlopStudioAgentBroker(this IServiceCollection services, AgentBrokerOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled || options.Stage == AgentToolExposureStage.None) return services;
        options.Validate();
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IAgentToolRegistry)))
            throw new InvalidOperationException("O registry de tools de agentes já foi composto.");
        services.AddSingleton(options);
        services.AddSingleton<MongoAgentFindSource>(provider => new MongoAgentFindSource(
            provider.GetRequiredService<IConnectionSecretStore>(), provider.GetService<IEnvironmentVaultRepository>(),
            provider.GetRequiredService<MongoClientPool>()));
        services.AddSingleton<IAgentToolRegistry>(provider =>
        {
            var literalQueries = options.Stage >= AgentToolExposureStage.LiteralQueries
                ? provider.GetRequiredService<MongoAgentFindSource>()
                : null;
            return new AgentToolRegistry(
                provider.GetRequiredService<IConnectionProfileRepository>(),
                provider.GetRequiredService<IAgentAuthorizationPolicyProvider>(),
                provider.GetRequiredService<IAgentPermissionEvaluator>(),
                provider.GetRequiredService<IAgentAuditRepository>(),
                options.ToolExecutionTimeout,
                metadata: provider.GetRequiredService<IMongoMetadataSource>(),
                find: literalQueries,
                count: literalQueries,
                exposure: AgentToolExposure.Through(options.Stage),
                principalAuthority: provider.GetRequiredService<IAgentPrincipalAuthority>());
        });
        services.AddSingleton<AgentBrokerHost>(provider => new AgentBrokerHost(
            provider.GetRequiredService<IAgentToolRegistry>(), provider.GetRequiredService<IAgentPrincipalAuthority>(),
            options));
        return services;
    }

    internal static ISecretStore CreateAgentSecretStore(bool isWindows, bool isLinux) =>
        isWindows ? new WindowsCredentialSecretStore() :
        isLinux ? new LinuxSecretServiceSecretStore() :
        new UnavailableSecretStore();
}
