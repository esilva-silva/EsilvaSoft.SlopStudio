using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

public sealed class WorkspaceService(IConnectionProfileRepository profiles, IQueryHistoryRepository queryHistory, IScriptHistoryRepository scriptHistory, ISavedQueryRepository savedQueries, IAuditRepository audit, IMongoWorkspaceService mongo, IScriptExecutionService scripts, IScriptFileService scriptFiles, IConnectionSecretStore secrets, IEnvironmentVaultRepository? environments = null, IExplorerMetadataService? explorer = null, IConsoleRuntime? console = null, IConsoleHistoryRepository? consoleHistory = null, IApplicationOperationService? operations = null, ICodeFormatter? formatter = null, IResultPageExportService? resultExports = null)
{
    public Task ExportResultPageAsync(string path, IReadOnlyList<string> documents, bool csv, Action<int, int> progress, CancellationToken cancellationToken) =>
        (resultExports ?? throw new InvalidOperationException("Exportador indisponível.")).ExportAsync(path, documents, csv, progress, cancellationToken);

    public Task<string> FormatCodeAsync(string text, CancellationToken cancellationToken = default) =>
        (formatter ?? throw new InvalidOperationException("Formatador indisponível.")).FormatAsync(text, cancellationToken);

    public IApplicationOperationService Operations { get; } = operations ?? new ApplicationOperationService();

    private async Task<T> TrackAsync<T>(string description, Func<CancellationToken, Task<T>> action, ApplicationOperationPriority priority = ApplicationOperationPriority.Normal, CancellationToken token = default)
    {
        using var operation = Operations.Begin(description, priority, cancellationToken: token);
        try
        {
            var result = await action(operation.Token).ConfigureAwait(false);
            operation.Complete(result is ConnectionTestResult { IsSuccess: false } ? ApplicationOperationStatus.Error : ApplicationOperationStatus.Success,
                description + (result is ConnectionTestResult { IsSuccess: false } ? " — falha; consulte o diagnóstico da conexão" : " — concluído"));
            return result;
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
        { operation.Complete(ApplicationOperationStatus.Cancelled, description + " — cancelado"); throw; }
        catch
        { operation.Complete(ApplicationOperationStatus.Error, description + " — falha; consulte os detalhes da operação"); throw; }
    }

    private Task<bool> TrackAsync(string description, Func<CancellationToken, Task> action, ApplicationOperationPriority priority = ApplicationOperationPriority.Normal, CancellationToken token = default) =>
        TrackAsync(description, async cancellation => { await action(cancellation).ConfigureAwait(false); return true; }, priority, token: token);

    public Task<ConsoleExecutionResult> ExecuteConsoleAsync(ConsoleRequest request, Func<ConsoleWriteConfirmation, CancellationToken, Task<bool>> confirm, CancellationToken token = default) =>
        (console ?? throw new InvalidOperationException("Runtime Console indisponível.")).ExecuteAsync(request, confirm, token);
    public ConsoleStatement GetConsoleStatement(string script, int caret) =>
        (console ?? throw new InvalidOperationException("Runtime Console indisponível.")).GetStatement(script, caret);
    public Task<IReadOnlyList<ConsoleHistoryEntry>> GetConsoleHistoryAsync(CancellationToken token = default) =>
        TrackAsync("Carregando histórico do Console", operationToken => consoleHistory?.GetConsoleHistoryAsync(cancellationToken: operationToken) ?? Task.FromResult<IReadOnlyList<ConsoleHistoryEntry>>([]), token: token);
    public Task<TopologyInfo> GetExplorerTopologyAsync(ConnectionProfile profile, CancellationToken token = default) =>
        TrackAsync("Carregando topologia", operationToken => ExplorerMetadata.GetTopologyAsync(profile, operationToken), token: token);
    public Task<IReadOnlyList<IndexInfo>> GetExplorerIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken token = default) =>
        TrackAsync("Carregando índices", operationToken => ExplorerMetadata.GetIndexesAsync(profile, database, collection, operationToken), token: token);
    public Task<string> GetExplorerCollectionDetailsAsync(ConnectionProfile profile, string database, string collection, CancellationToken token = default) =>
        TrackAsync("Carregando detalhes da coleção", operationToken => ExplorerMetadata.GetCollectionDetailsAsync(profile, database, collection, operationToken), token: token);
    private IExplorerMetadataService ExplorerMetadata => explorer ?? throw new InvalidOperationException("Serviço de metadados do explorer indisponível.");

    public EnvironmentVault LoadEnvironments() => environments?.LoadEnvironments() ?? EnvironmentVault.CreateDefault();
    public Task SaveEnvironmentsAsync(EnvironmentVault vault, CancellationToken cancellationToken = default) =>
        TrackAsync("Salvando ambientes", token => Task.Run(() =>
            (environments ?? throw new InvalidOperationException("Armazenamento de ambientes indisponível.")).SaveEnvironments(vault), token),
            ApplicationOperationPriority.High, cancellationToken);

    public Task<IReadOnlyList<ConnectionProfile>> GetProfilesAsync(CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando conexões locais", operationToken => profiles.GetAllAsync(operationToken), token: cancellationToken);

    public Task SaveProfileAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        TrackAsync("Salvando conexão", operationToken => profiles.SaveAsync(profile, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public void SaveConnectionPassword(Guid profileId, string password) => secrets.SetPassword(profileId, password);

    public async Task DeleteProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        await profiles.DeleteAsync(profileId, cancellationToken).ConfigureAwait(false);
        secrets.Remove(profileId);
    }

    public Task<IReadOnlyList<QueryHistoryEntry>> GetRecentQueryHistoryAsync(Guid? profileId, int maximum = 50, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando histórico", operationToken => queryHistory.GetRecentAsync(profileId, maximum, operationToken), token: cancellationToken);

    public Task SaveQueryHistoryAsync(QueryHistoryEntry entry, CancellationToken cancellationToken = default) =>
        queryHistory.SaveAsync(entry, cancellationToken);

    public Task<IReadOnlyList<ScriptHistoryEntry>> GetRecentScriptHistoryAsync(int maximum = 50, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando histórico de arquivos", operationToken => scriptHistory.GetRecentAsync(maximum, operationToken), token: cancellationToken);

    public Task SaveScriptHistoryAsync(ScriptHistoryEntry entry, CancellationToken cancellationToken = default) =>
        scriptHistory.SaveAsync(entry, cancellationToken);

    public Task<IReadOnlyList<SavedQuery>> GetSavedQueriesAsync(Guid? profileId, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando consultas salvas", operationToken => savedQueries.GetAllAsync(profileId, operationToken), token: cancellationToken);

    public Task SaveSavedQueryAsync(SavedQuery query, CancellationToken cancellationToken = default) =>
        TrackAsync("Salvando consulta", operationToken => savedQueries.SaveAsync(query, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task DeleteSavedQueryAsync(Guid id, CancellationToken cancellationToken = default) =>
        TrackAsync("Excluindo consulta salva", operationToken => savedQueries.DeleteSavedAsync(id, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<IReadOnlyList<AuditEntry>> GetRecentAuditAsync(int maximum = 100, CancellationToken cancellationToken = default) =>
        audit.GetRecentAuditAsync(maximum, cancellationToken);

    public Task SaveAuditAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
        audit.SaveAsync(entry, cancellationToken);

    public Task<ConnectionTestResult> TestConnectionAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        TrackAsync("Conectando ao MongoDB", operationToken => mongo.TestConnectionAsync(profile, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<IReadOnlyList<string>> GetDatabasesAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        TrackAsync($"Carregando bancos — {profile.Name}", operationToken => mongo.GetDatabaseNamesAsync(profile, operationToken), ApplicationOperationPriority.High, cancellationToken);

    public Task<IReadOnlyList<string>> GetCollectionsAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando coleções", operationToken => mongo.GetCollectionNamesAsync(profile, database, operationToken), token: cancellationToken);

    public Task CreateDatabaseAsync(ConnectionProfile profile, DatabaseCreateRequest request, CancellationToken cancellationToken = default) =>
        mongo.CreateDatabaseAsync(profile, request, cancellationToken);

    public Task CreateCollectionAsync(ConnectionProfile profile, CollectionCreateRequest request, CancellationToken cancellationToken = default) =>
        mongo.CreateCollectionAsync(profile, request, cancellationToken);

    public Task RenameCollectionAsync(ConnectionProfile profile, CollectionRenameRequest request, CancellationToken cancellationToken = default) =>
        mongo.RenameCollectionAsync(profile, request, cancellationToken);

    public Task UpdateViewAsync(ConnectionProfile profile, ViewUpdateRequest request, CancellationToken cancellationToken = default) =>
        mongo.UpdateViewAsync(profile, request, cancellationToken);

    public Task ConfigureCollectionValidationAsync(ConnectionProfile profile, CollectionValidationRequest request, CancellationToken cancellationToken = default) =>
        mongo.ConfigureCollectionValidationAsync(profile, request, cancellationToken);

    public Task<CollectionValidationInfo> GetCollectionValidationAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default) =>
        mongo.GetCollectionValidationAsync(profile, database, collection, cancellationToken);

    public Task DropCollectionAsync(ConnectionProfile profile, CollectionDropRequest request, CancellationToken cancellationToken = default) =>
        mongo.DropCollectionAsync(profile, request, cancellationToken);

    public Task DropDatabaseAsync(ConnectionProfile profile, DatabaseDropRequest request, CancellationToken cancellationToken = default) =>
        mongo.DropDatabaseAsync(profile, request, cancellationToken);

    public Task<string> GetServerStatusAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando estado do servidor", operationToken => mongo.GetServerStatusAsync(profile, operationToken), token: cancellationToken);

    public Task<string> GetTopologyAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        mongo.GetTopologyAsync(profile, cancellationToken);

    public Task<string> GetCurrentOperationsAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        mongo.GetCurrentOperationsAsync(profile, cancellationToken);

    public Task<string> GetProfilerStatusAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken = default) =>
        mongo.GetProfilerStatusAsync(profile, database, cancellationToken);

    public Task KillOperationAsync(ConnectionProfile profile, OperationKillRequest request, CancellationToken cancellationToken = default) =>
        mongo.KillOperationAsync(profile, request, cancellationToken);

    public Task<string> ValidateCollectionIntegrityAsync(ConnectionProfile profile, CollectionIntegrityCheckRequest request, CancellationToken cancellationToken = default) =>
        mongo.ValidateCollectionIntegrityAsync(profile, request, cancellationToken);

    public Task<string> CompactCollectionAsync(ConnectionProfile profile, CollectionCompactRequest request, CancellationToken cancellationToken = default) =>
        mongo.CompactCollectionAsync(profile, request, cancellationToken);

    public Task<string> GetUsersAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        mongo.GetUsersAsync(profile, cancellationToken);

    public Task CreateUserAsync(ConnectionProfile profile, DatabaseUserCreateRequest request, CancellationToken cancellationToken = default) =>
        mongo.CreateUserAsync(profile, request, cancellationToken);

    public Task DropUserAsync(ConnectionProfile profile, DatabaseUserDropRequest request, CancellationToken cancellationToken = default) =>
        mongo.DropUserAsync(profile, request, cancellationToken);

    public Task UpdateUserRolesAsync(ConnectionProfile profile, DatabaseUserRoleRequest request, CancellationToken cancellationToken = default) =>
        mongo.UpdateUserRolesAsync(profile, request, cancellationToken);

    public Task<string> GetRolesAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        mongo.GetRolesAsync(profile, cancellationToken);

    public Task<string> GetDatabaseStatsAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando estatísticas do banco", operationToken => mongo.GetDatabaseStatsAsync(profile, database, operationToken), token: cancellationToken);

    public Task<string> GetCollectionStatsAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando estatísticas da coleção", operationToken => mongo.GetCollectionStatsAsync(profile, database, collection, operationToken), token: cancellationToken);

    public Task<QueryPage> QueryAsync(ConnectionProfile profile, MongoQuery query, CancellationToken cancellationToken = default) =>
        TrackAsync($"Carregando página {query.Skip / Math.Max(1, query.Limit) + 1} — {profile.Name} › {query.Database} › {query.Collection}", operationToken => mongo.QueryAsync(profile, query, operationToken), ApplicationOperationPriority.High, cancellationToken);

    public Task<CollectionCountResult> CountDocumentsAsync(ConnectionProfile profile, CollectionCountRequest request, CancellationToken cancellationToken = default) =>
        TrackAsync("Contando documentos", operationToken => mongo.CountDocumentsAsync(profile, request, operationToken), token: cancellationToken);

    public Task<DistinctValuesResult> GetDistinctValuesAsync(ConnectionProfile profile, DistinctValuesRequest request, CancellationToken cancellationToken = default) =>
        TrackAsync("Consultando valores distintos", operationToken => mongo.GetDistinctValuesAsync(profile, request, operationToken), token: cancellationToken);

    public Task<string> ExplainAsync(ConnectionProfile profile, MongoQuery query, CancellationToken cancellationToken = default) =>
        TrackAsync("Analisando consulta", operationToken => mongo.ExplainAsync(profile, query, operationToken), token: cancellationToken);

    public Task<QueryPage> AggregateAsync(ConnectionProfile profile, AggregationQuery query, CancellationToken cancellationToken = default) =>
        TrackAsync($"Executando agregação — {profile.Name} › {query.Database} › {query.Collection}", operationToken => mongo.AggregateAsync(profile, query, operationToken), ApplicationOperationPriority.High, cancellationToken);

    public Task<DatabaseExportResult> ExportDatabaseAsync(ConnectionProfile profile, DatabaseExportRequest request, CancellationToken cancellationToken = default) =>
        mongo.ExportDatabaseAsync(profile, request, cancellationToken);

    public Task<DatabaseImportResult> ImportDatabaseAsync(ConnectionProfile profile, DatabaseImportRequest request, CancellationToken cancellationToken = default) =>
        mongo.ImportDatabaseAsync(profile, request, cancellationToken);

    public Task<DocumentMutationResult> InsertAsync(ConnectionProfile profile, string database, string collection, string documentJson, CancellationToken cancellationToken = default) =>
        TrackAsync("Inserindo documento", operationToken => mongo.InsertAsync(profile, database, collection, documentJson, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<long> InsertManyAsync(ConnectionProfile profile, BulkInsertRequest request, CancellationToken cancellationToken = default) =>
        TrackAsync("Inserindo documentos", operationToken => mongo.InsertManyAsync(profile, request, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<DocumentMutationResult> ReplaceAsync(ConnectionProfile profile, string database, string collection, string filterJson, string documentJson, CancellationToken cancellationToken = default) =>
        TrackAsync("Salvando documento", operationToken => mongo.ReplaceAsync(profile, database, collection, filterJson, documentJson, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<DocumentMutationResult> UpdateAsync(ConnectionProfile profile, DocumentUpdateRequest request, CancellationToken cancellationToken = default) =>
        TrackAsync("Atualizando documentos", operationToken => mongo.UpdateAsync(profile, request, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<FindAndModifyResult> FindAndModifyAsync(ConnectionProfile profile, DocumentUpdateRequest request, CancellationToken cancellationToken = default) =>
        TrackAsync("Atualizando documento", operationToken => mongo.FindAndModifyAsync(profile, request, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<DocumentMutationResult> DeleteAsync(ConnectionProfile profile, string database, string collection, string filterJson, CancellationToken cancellationToken = default) =>
        TrackAsync("Excluindo documento", operationToken => mongo.DeleteAsync(profile, database, collection, filterJson, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<DocumentMutationResult> DeleteManyAsync(ConnectionProfile profile, string database, string collection, string filterJson, CancellationToken cancellationToken = default) =>
        TrackAsync("Excluindo documentos", operationToken => mongo.DeleteManyAsync(profile, database, collection, filterJson, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<IReadOnlyList<string>> GetIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando índices", operationToken => mongo.GetIndexesAsync(profile, database, collection, operationToken), token: cancellationToken);

    public Task<IReadOnlyList<string>> GetIndexUsageStatsAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando uso de índices", operationToken => mongo.GetIndexUsageStatsAsync(profile, database, collection, operationToken), token: cancellationToken);

    public Task<string> CreateIndexAsync(ConnectionProfile profile, IndexCreateRequest request, CancellationToken cancellationToken = default) =>
        mongo.CreateIndexAsync(profile, request, cancellationToken);

    public Task SetIndexVisibilityAsync(ConnectionProfile profile, IndexVisibilityRequest request, CancellationToken cancellationToken = default) =>
        mongo.SetIndexVisibilityAsync(profile, request, cancellationToken);

    public Task DropIndexAsync(ConnectionProfile profile, IndexDropRequest request, CancellationToken cancellationToken = default) =>
        mongo.DropIndexAsync(profile, request, cancellationToken);

    public Task<ScriptExecutionResult> ExecuteScriptAsync(ConnectionProfile profile, string script, string? inputJson, string? database = null, CancellationToken cancellationToken = default) =>
        scripts.ExecuteAsync(profile, script, inputJson, database, cancellationToken);

    public Task SaveScriptAsync(string path, string script, CancellationToken cancellationToken = default) =>
        TrackAsync("Salvando arquivo", operationToken => scriptFiles.SaveAsync(path, script, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<string> LoadScriptAsync(string path, CancellationToken cancellationToken = default) =>
        TrackAsync("Abrindo arquivo", operationToken => scriptFiles.LoadAsync(path, operationToken), token: cancellationToken);
}
