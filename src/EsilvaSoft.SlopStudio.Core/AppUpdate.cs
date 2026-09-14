using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Release version with SemVer 2 precedence; build metadata (<c>+sha</c>) never affects ordering.</summary>
public sealed class AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    private readonly string[] _prerelease;

    private AppVersion(int major, int minor, int patch, string[] prerelease)
    {
        Major = major; Minor = minor; Patch = patch; _prerelease = prerelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public bool IsPrerelease => _prerelease.Length > 0;

    public static AppVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"Versão inválida: '{text}'.");

    /// <summary>Accepts an optional leading <c>v</c>, as in release tags.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out AppVersion? version)
    {
        version = null;
        var value = text?.Trim() ?? "";
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        if (value.IndexOf('+') is var plus and >= 0) value = value[..plus];
        string[] prerelease = [];
        if (value.IndexOf('-') is var dash and >= 0)
        {
            prerelease = value[(dash + 1)..].Split('.');
            value = value[..dash];
            if (prerelease.Any(identifier => identifier.Length == 0 || !identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
                || IsNumeric(identifier) && !HasNoLeadingZero(identifier))) return false;
        }
        var parts = value.Split('.');
        if (parts.Length != 3 || !TryParseNumber(parts[0], out var major) || !TryParseNumber(parts[1], out var minor) || !TryParseNumber(parts[2], out var patch))
            return false;
        version = new(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(AppVersion? other)
    {
        if (other is null) return 1;
        var result = Major.CompareTo(other.Major);
        if (result == 0) result = Minor.CompareTo(other.Minor);
        if (result == 0) result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;
        // A final release ranks above any pre-release of the same core version.
        if (_prerelease.Length == 0 || other._prerelease.Length == 0) return other._prerelease.Length.CompareTo(_prerelease.Length);
        for (var index = 0; index < Math.Min(_prerelease.Length, other._prerelease.Length); index++)
        {
            result = CompareIdentifier(_prerelease[index], other._prerelease[index]);
            if (result != 0) return result;
        }
        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }

    public bool Equals(AppVersion? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is AppVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', _prerelease));
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Major}.{Minor}.{Patch}{(_prerelease.Length == 0 ? "" : "-" + string.Join('.', _prerelease))}");

    public static bool operator ==(AppVersion? left, AppVersion? right) => left is null ? right is null : left.Equals(right);
    public static bool operator !=(AppVersion? left, AppVersion? right) => !(left == right);
    public static bool operator <(AppVersion? left, AppVersion? right) => left is null ? right is not null : left.CompareTo(right) < 0;
    public static bool operator <=(AppVersion? left, AppVersion? right) => left is null || left.CompareTo(right) <= 0;
    public static bool operator >(AppVersion? left, AppVersion? right) => left is not null && left.CompareTo(right) > 0;
    public static bool operator >=(AppVersion? left, AppVersion? right) => left is null ? right is null : left.CompareTo(right) >= 0;

    private static bool TryParseNumber(string part, out int value)
    {
        value = 0;
        return IsNumeric(part) && HasNoLeadingZero(part) && int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsNumeric(string part) => part.Length > 0 && part.All(char.IsAsciiDigit);
    private static bool HasNoLeadingZero(string part) => part == "0" || part[0] != '0';

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = IsNumeric(left);
        var rightNumeric = IsNumeric(right);
        if (leftNumeric && rightNumeric)
            return left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
        return leftNumeric ? -1 : rightNumeric ? 1 : string.CompareOrdinal(left, right);
    }
}

public sealed record AppReleaseAsset(string Name, Uri DownloadUrl, long Size, string? Digest);

public sealed record AppReleaseCandidate(string Tag, bool IsDraft, bool IsPrerelease, Uri PageUrl, IReadOnlyList<AppReleaseAsset> Assets);

/// <summary>The package of a newer release that matches the running platform.</summary>
public sealed record AppUpdateRelease(AppVersion Version, string Tag, string AssetName, Uri DownloadUrl, long Size, string? Sha256, Uri PageUrl, Uri? ChecksumsUrl);

public static class AppUpdateSelector
{
    public const string ChecksumsAssetName = "SHA256SUMS.txt";

    /// <summary>Mirrors the package names produced by release.yml and build-release.ps1.</summary>
    public static string GetAssetName(AppVersion version, string rid)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(rid);
        return $"EsilvaSoft.SlopStudio-{version}-{rid}{(rid.StartsWith("win-", StringComparison.Ordinal) ? ".zip" : ".tar.gz")}";
    }

    /// <summary>
    /// Newest non-draft release above <paramref name="current"/> that publishes a package for <paramref name="rid"/>.
    /// Pre-releases are offered only to an installation that already runs a pre-release.
    /// </summary>
    public static AppUpdateRelease? SelectNewest(AppVersion current, IEnumerable<AppReleaseCandidate> releases, string rid)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(releases);
        AppUpdateRelease? newest = null;
        foreach (var release in releases)
        {
            if (release.IsDraft || !AppVersion.TryParse(release.Tag, out var version)) continue;
            if ((release.IsPrerelease || version.IsPrerelease) && !current.IsPrerelease) continue;
            if (version <= current || newest is not null && version <= newest.Version) continue;
            var name = GetAssetName(version, rid);
            if (release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, name, StringComparison.Ordinal)) is not { } package) continue;
            var checksums = release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, ChecksumsAssetName, StringComparison.Ordinal));
            newest = new(version, release.Tag, package.Name, package.DownloadUrl, package.Size, NormalizeSha256(package.Digest), release.PageUrl, checksums?.DownloadUrl);
        }
        return newest;
    }

    /// <summary>Reads GitHub's <c>sha256:hex</c> asset digest; anything else is treated as absent.</summary>
    public static string? NormalizeSha256(string? digest)
    {
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var hex = digest[prefix.Length..];
        return hex.Length == 64 && hex.All(char.IsAsciiHexDigit) ? hex.ToLowerInvariant() : null;
    }

    /// <summary>Finds a package in a <c>sha256sum</c> listing ("hash  name" or "hash *name").</summary>
    public static string? FindChecksum(string listing, string assetName)
    {
        ArgumentNullException.ThrowIfNull(listing);
        foreach (var line in listing.Split('\n'))
        {
            var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && string.Equals(parts[1].TrimStart('*'), assetName, StringComparison.Ordinal))
                return NormalizeSha256("sha256:" + parts[0]);
        }
        return null;
    }
}
