using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Production context capture of one turn. It works only on the immutable request the tab captured before awaiting,
/// shares exactly the fields the caller included (consent is expressed by what is present), and publishes the
/// connection only by its logical ID after confirming that ID belongs to a registered profile. Profile names, URIs,
/// credentials and query results never enter the snapshot; connection strings typed in the shared text are redacted.
/// Namespace parts without their parent are dropped, never guessed. Failures are visible as
/// <see cref="AgentRuntimeException"/> with a safe code.
/// </summary>
public sealed partial class AgentContextProvider : IAgentContextProvider
{
    /// <summary>Upper bound for the shared editor text (selection plus editor content).</summary>
    public const int MaximumSharedTextChars = 32 * 1024;

    private const int MaximumNameChars = 255;
    private const int MaximumTabIdChars = 128;
    private const string RedactedConnectionString = "[connection string removida]";

    private readonly IConnectionProfileRepository _profiles;
    private readonly TimeProvider _time;

    public AgentContextProvider(IConnectionProfileRepository profiles, TimeProvider? time = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _time = time ?? TimeProvider.System;
    }

    public async Task<AgentContextSnapshot> CaptureAsync(AgentContextCaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Every field is read once, here, before the only await; nothing mutable is consulted afterwards.
        var tabId = request.TabId;
        var version = request.DocumentVersion;
        var connectionText = request.ConnectionId;
        var database = request.DatabaseName;
        var collection = request.CollectionName;
        var selection = request.SelectedText;
        var editor = request.EditorText;

        if (string.IsNullOrWhiteSpace(tabId) || tabId.Length > MaximumTabIdChars || tabId.Any(char.IsControl) || version < 0)
        {
            throw new AgentRuntimeException("ContextInvalid", "Context request is invalid.");
        }

        if ((selection?.Length ?? 0) + (editor?.Length ?? 0) > MaximumSharedTextChars)
        {
            throw new AgentRuntimeException("ContextTooLarge", "Shared text exceeds the context limit.");
        }

        Guid? connectionId = null;
        if (!string.IsNullOrWhiteSpace(connectionText))
        {
            // Only a logical ID may leave the tab; anything else (for example a URI) is refused, not forwarded.
            if (!Guid.TryParse(connectionText, out var parsed) || parsed == Guid.Empty)
            {
                throw new AgentRuntimeException("ContextConnectionInvalid", "Connection reference is invalid.");
            }

            connectionId = parsed;
        }

        database = connectionId is null ? null : NormalizeName(database);
        collection = database is null ? null : NormalizeName(collection);

        if (connectionId is { } id && !await IsRegisteredAsync(id, cancellationToken).ConfigureAwait(false))
        {
            throw new AgentRuntimeException("ContextConnectionUnknown", "Connection is not registered.");
        }

        var connection = connectionId?.ToString("D", CultureInfo.InvariantCulture);
        return new AgentContextSnapshot(tabId, version, _time.GetUtcNow(), connection, database, collection,
            BuildAuthorizedContext(connection, database, collection, Redact(selection), Redact(editor)));
    }

    private async Task<bool> IsRegisteredAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Core.ConnectionProfile> profiles;
        try
        {
            profiles = await _profiles.GetAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AgentRuntimeException("ContextUnavailable", "Connection profiles are unavailable.");
        }

        // Only the ID is compared; no other profile field is read or copied.
        return profiles is not null && profiles.Any(profile => profile is not null && profile.Id == connectionId);
    }

    private static string? NormalizeName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Length > MaximumNameChars || name.Any(char.IsControl) ? null : name;

    private static string? Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            return ConnectionStringPattern().Replace(text, RedactedConnectionString);
        }
        catch (RegexMatchTimeoutException)
        {
            // Unredacted text is never shared: fail closed.
            throw new AgentRuntimeException("ContextRedactionFailed", "Shared text could not be sanitized.");
        }
    }

    private static string? BuildAuthorizedContext(
        string? connection, string? database, string? collection, string? selection, string? editor)
    {
        var builder = new StringBuilder();
        if (connection is not null)
        {
            builder.Append("Conexão (ID lógico): ").Append(connection).Append('\n');
        }

        if (database is not null)
        {
            builder.Append("Banco: ").Append(database).Append('\n');
        }

        if (collection is not null)
        {
            builder.Append("Coleção: ").Append(collection).Append('\n');
        }

        if (selection is not null)
        {
            AppendBlock(builder, "Seleção do editor:", selection);
        }

        if (editor is not null)
        {
            AppendBlock(builder, "Conteúdo do editor:", editor);
        }

        return builder.Length == 0 ? null : builder.ToString().TrimEnd('\n');
    }

    private static void AppendBlock(StringBuilder builder, string label, string text)
    {
        if (builder.Length > 0)
        {
            builder.Append('\n');
        }

        builder.Append(label).Append('\n').Append(text).Append('\n');
    }

    [GeneratedRegex(@"mongodb(?:\+srv)?://[^\s""'`<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ConnectionStringPattern();
}
