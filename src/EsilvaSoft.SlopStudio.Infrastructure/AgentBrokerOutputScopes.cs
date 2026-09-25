using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>
/// Trusted classification of the data each catalog tool releases. It is chosen by the broker, never by the client,
/// and the registry still requires a grant for exactly this scope and destination.
/// </summary>
internal static class AgentBrokerOutputScopes
{
    public static AgentOutputDataScope? For(string? toolName) => toolName switch
    {
        AgentToolRegistry.ListConnectionsToolName or AgentToolRegistry.ListDatabasesToolName or
            AgentToolRegistry.ListCollectionsToolName or AgentToolRegistry.GetIndexesToolName =>
            AgentOutputDataScope.Metadata,
        AgentToolRegistry.GetCollectionSchemaToolName => AgentOutputDataScope.Schema,
        AgentToolRegistry.MongoExplainToolName or AgentToolRegistry.MongoFindToolName or AgentToolRegistry.MongoCountToolName or
            AgentToolRegistry.SampleDocumentsToolName or AgentToolRegistry.MongoFindOneToolName or
            AgentToolRegistry.GetDocumentToolName or AgentToolRegistry.MongoDistinctToolName =>
            AgentOutputDataScope.DocumentValues,
        _ => null
    };
}
