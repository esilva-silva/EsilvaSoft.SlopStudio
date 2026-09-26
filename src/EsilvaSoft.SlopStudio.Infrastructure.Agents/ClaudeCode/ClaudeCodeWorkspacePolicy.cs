namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;

/// <summary>
/// Escolha do cwd do turno (ADR-054 revisada): a pasta de workspace do usuário quando ela é segura; senão a pasta
/// dedicada vazia do app. Nunca o diretório de dados, o LiteDB, <c>~/.claude</c>, <c>~/.ssh</c>, o perfil inteiro ou
/// a raiz de um volume — nem uma pasta que contenha algum deles.
/// </summary>
internal static class ClaudeCodeWorkspacePolicy
{
    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static (string Directory, ClaudeCodeWorkingDirectoryKind Kind) Resolve(ClaudeCodeAgentProviderOptions options, string homeDirectory)
    {
        var appData = Normalize(options.ResolveAppDataDirectory());
        var database = Normalize(options.ResolveDatabasePath());
        var home = Normalize(homeDirectory);
        string[] protectedPaths = [appData, database, Path.Combine(home, ".claude"), Path.Combine(home, ".ssh")];

        string? requested = null;
        try
        {
            requested = options.WorkspaceDirectory?.Invoke();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            requested = null;
        }

        if (TryAcceptWorkspace(requested, home, protectedPaths) is { } workspace)
        {
            return (workspace, ClaudeCodeWorkingDirectoryKind.Workspace);
        }

        var dedicated = Normalize(options.ResolveDedicatedWorkingDirectory());
        if (!IsAcceptable(dedicated, home, protectedPaths))
        {
            throw new InvalidOperationException("A pasta dedicada do Claude Code não pode ficar em área protegida.");
        }

        Directory.CreateDirectory(dedicated);
        return (dedicated, ClaudeCodeWorkingDirectoryKind.Dedicated);
    }

    internal static string? TryAcceptWorkspace(string? requested, string home, IReadOnlyList<string> protectedPaths)
    {
        if (string.IsNullOrWhiteSpace(requested) || !Path.IsPathFullyQualified(requested) || requested.Any(char.IsControl))
        {
            return null;
        }

        string full;
        try
        {
            full = Normalize(requested);
            if (!Directory.Exists(full))
            {
                return null;
            }

            // Um link simbólico é avaliado pelo destino final: o apelido não contorna as áreas protegidas.
            if (new DirectoryInfo(full).ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                full = Normalize(target.FullName);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        return IsAcceptable(full, home, protectedPaths) ? full : null;
    }

    private static bool IsAcceptable(string full, string home, IReadOnlyList<string> protectedPaths)
    {
        if (PathEquals(full, Path.GetPathRoot(full) ?? string.Empty) || PathEquals(full, home))
        {
            return false;
        }

        return !protectedPaths.Any(protectedPath => IsSameOrInside(full, protectedPath) || IsSameOrInside(protectedPath, full));
    }

    internal static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal static bool PathEquals(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right), Comparison);

    /// <summary>True quando <paramref name="path"/> é <paramref name="container"/> ou está dentro dele.</summary>
    internal static bool IsSameOrInside(string path, string container)
    {
        var normalizedPath = Path.TrimEndingDirectorySeparator(path);
        var normalizedContainer = Path.TrimEndingDirectorySeparator(container);
        if (PathEquals(normalizedPath, normalizedContainer))
        {
            return true;
        }

        var prefix = normalizedContainer.EndsWith(Path.DirectorySeparatorChar) ? normalizedContainer : normalizedContainer + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(prefix, Comparison);
    }
}
