namespace EsilvaSoft.SlopStudio.Core;

public sealed record AppReleaseAsset(string Name, Uri DownloadUrl, long Size, string? Digest);
