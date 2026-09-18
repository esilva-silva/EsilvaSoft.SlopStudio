using EsilvaSoft.SlopStudio.Application;
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
        services.AddSingleton<LiteDbConnectionProfileRepository>(_ => new LiteDbConnectionProfileRepository(workspaceDatabasePath));
        services.AddSingleton<IConnectionProfileRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IQueryHistoryRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IScriptHistoryRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<ISavedQueryRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IAuditRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IWorkspaceSessionRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IEnvironmentVaultRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IConsoleHistoryRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IConsoleDatabaseSessionFactory, ConsoleDatabaseSessionFactory>();
        services.AddSingleton<IConsoleRuntime, ConsoleRuntime>();
        services.AddSingleton<IConnectionSecretStore, SessionConnectionSecretStore>();
        services.AddSingleton<IMongoWorkspaceService, MongoWorkspaceService>();
        // Autocomplete knowledge: invalidations from IDE operations, driver metadata and the in-memory catalog.
        services.AddSingleton<IMetadataInvalidationBus, MetadataInvalidationBus>();
        services.AddSingleton<IMongoMetadataSource, MongoMetadataSource>();
        services.AddSingleton<IMetadataCache>(provider => new MetadataCache(provider.GetRequiredService<IMongoMetadataSource>(),
            provider.GetRequiredService<IApplicationOperationService>(), provider.GetRequiredService<IMetadataInvalidationBus>()));
        services.AddSingleton<ICatalogSource>(_ => new LanguageCatalogSource());
        services.AddSingleton<ICatalogSource>(provider => new MetadataCatalogSource(provider.GetRequiredService<IMetadataCache>()));
        services.AddSingleton<IKnowledgeCatalog, KnowledgeCatalog>();
        services.AddSingleton<IExplorerMetadataService, ExplorerMetadataService>();
        services.AddSingleton<IScriptExecutionService, MongoshScriptExecutionService>();
        services.AddSingleton<IScriptFileService, LocalScriptFileService>();
        services.AddSingleton<IAppUpdateService>(_ => new GitHubAppUpdateService(AppUpdateOptions.FromProcess()));
        return services;
    }
}
