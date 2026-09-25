namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Release stages of the read-only catalog, in the order of the phase 7 plan (lote 2). A later stage always
/// includes the earlier ones. Each stage may be enabled only after its security gate has evidence; composing the
/// registry in DI does not release anything by itself.
/// </summary>
public enum AgentToolExposureStage
{
    /// <summary>No tool is discoverable or executable. Default for every registry instance.</summary>
    None = 0,

    /// <summary><c>list_connections</c>, <c>list_databases</c> and <c>list_collections</c>.</summary>
    Metadata = 1,

    /// <summary>Adds the literal query codec tools <c>mongo_find</c> and <c>mongo_count</c>.</summary>
    LiteralQueries = 2,

    /// <summary>Adds schema, sample, distinct, explain, indexes, find-one and get-document.</summary>
    DerivedReads = 3
}

/// <summary>
/// Immutable, closed exposure gate for one registry. Unreleased tools are indistinguishable from unknown tools:
/// they have no descriptor or schema and are rejected before audit, policy, profile or MongoDB access.
/// </summary>
public sealed class AgentToolExposure
{
    private AgentToolExposure(AgentToolExposureStage stage) => Stage = stage;

    public static AgentToolExposure None { get; } = new(AgentToolExposureStage.None);

    public AgentToolExposureStage Stage { get; }

    public static AgentToolExposure Through(AgentToolExposureStage stage) =>
        Enum.IsDefined(stage) ? stage == AgentToolExposureStage.None ? None : new(stage)
            : throw new ArgumentOutOfRangeException(nameof(stage));

    /// <summary>The stage that introduces a catalog tool, or <see langword="null"/> for unknown names.</summary>
    public static AgentToolExposureStage? StageOf(string? toolName) => toolName switch
    {
        AgentToolRegistry.ListConnectionsToolName or AgentToolRegistry.ListDatabasesToolName or
            AgentToolRegistry.ListCollectionsToolName => AgentToolExposureStage.Metadata,
        AgentToolRegistry.MongoFindToolName or AgentToolRegistry.MongoCountToolName =>
            AgentToolExposureStage.LiteralQueries,
        AgentToolRegistry.GetCollectionSchemaToolName or AgentToolRegistry.SampleDocumentsToolName or
            AgentToolRegistry.MongoDistinctToolName or AgentToolRegistry.MongoExplainToolName or
            AgentToolRegistry.GetIndexesToolName or AgentToolRegistry.MongoFindOneToolName or
            AgentToolRegistry.GetDocumentToolName => AgentToolExposureStage.DerivedReads,
        _ => null
    };

    public bool Exposes(string? toolName) =>
        Stage != AgentToolExposureStage.None && StageOf(toolName) is { } stage && stage <= Stage;
}
