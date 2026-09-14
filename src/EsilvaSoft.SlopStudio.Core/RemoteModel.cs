namespace EsilvaSoft.SlopStudio.Core;

/// <summary>A published file: <paramref name="Sha256"/> is the LFS content hash when present; <paramref name="GitBlobSha1"/> is the git object id.</summary>
public sealed record RemoteModelFile(string Path, long Size, string? Sha256, string GitBlobSha1);

/// <summary>A self-contained ONNX GenAI export published as a top-level folder of a model repository, pinned to one commit.</summary>
public sealed record RemoteModelVariant(string Repository, string Revision, string Variant, string FolderName, long SizeBytes, string? License,
    Uri PageUrl, IReadOnlyList<RemoteModelFile> Files);

public sealed record RemoteModelProgress(long CompletedBytes, long TotalBytes, string CurrentFile);
