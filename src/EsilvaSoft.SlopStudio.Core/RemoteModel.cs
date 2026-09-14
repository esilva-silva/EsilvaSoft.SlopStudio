namespace EsilvaSoft.SlopStudio.Core;

/// <summary>A published file: <paramref name="Sha256"/> is the LFS content hash when present; <paramref name="GitBlobSha1"/> is the git object id.</summary>
public sealed record RemoteModelFile(string Path, long Size, string? Sha256, string GitBlobSha1);

/// <summary>A self-contained ONNX GenAI export published as a top-level folder of a model repository, pinned to one commit.</summary>
public sealed record RemoteModelVariant(string Repository, string Revision, string Variant, string FolderName, long SizeBytes, string? License,
    Uri PageUrl, IReadOnlyList<RemoteModelFile> Files)
{
    /// <summary>Model family shown to the user, such as SlopCoder-Mongo-1.5B-full; empty when unknown.</summary>
    public string Family { get; init; } = "";

    /// <summary>When to choose this variant, as stated by the publisher.</summary>
    public string? Hint { get; init; }

    /// <summary>Card of the source transformers model: prompt format, training data and limitations.</summary>
    public Uri? BaseModelUrl { get; init; }

    public ModelFlavor? Flavor => ModelDisplayNames.ParseVariant(Variant);

    public string Title => Family.Length == 0 ? FolderName
        : Flavor is { } flavor ? ModelDisplayNames.Title(Family, flavor) : $"{Family} — {Variant}";
}

public sealed record RemoteModelProgress(long CompletedBytes, long TotalBytes, string CurrentFile);
