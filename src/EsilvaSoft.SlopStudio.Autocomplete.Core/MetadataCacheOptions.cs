namespace EsilvaSoft.SlopStudio.Autocomplete.Core;

public sealed record MetadataCacheOptions
{
    public TimeSpan DatabasesTtl { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan CollectionsTtl { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan DefinitionTtl { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan IndexesTtl { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan SampledSchemaTtl { get; init; } = TimeSpan.FromMinutes(30);
    /// <summary>Delay before retrying after consecutive failures; the last value repeats.</summary>
    public IReadOnlyList<TimeSpan> Backoff { get; init; } = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];
    /// <summary>LRU bound for index and sampled schema entries per connection.</summary>
    public int CollectionScopedEntriesPerConnection { get; init; } = 64;
    public int SchemaMaximumDepth { get; init; } = 12;
    public int SchemaMaximumNodes { get; init; } = 10_000;
}
