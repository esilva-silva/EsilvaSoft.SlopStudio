namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>When to choose a published folder; the list order is the display order.</summary>
public sealed record RemoteModelVariantHint(string Folder, string Hint);

/// <summary>A repository of ONNX GenAI exports and the transformers repository it was exported from.</summary>
public sealed record RemoteModelRepository(string Repository, string? BaseRepository, IReadOnlyList<RemoteModelVariantHint> Variants);
