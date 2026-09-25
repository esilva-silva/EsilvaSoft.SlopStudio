using EsilvaSoft.SlopStudio.Core;
using LiteDB;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Closed version-one schema. No tolerant/defaulting deserialization at the consent boundary.</summary>
internal static class AgentSchemaSamplingConsentDocumentCodec
{
    internal const int MaximumConsents = 1024;

    internal static BsonDocument Encode(AgentSchemaSamplingConsentSnapshot consent) => new()
    {
        ["_id"] = consent.PrincipalId,
        ["schemaVersion"] = consent.SchemaVersion,
        ["revision"] = consent.Revision,
        ["updatedAtUtc"] = DateTime.UtcNow,
        ["consents"] = new BsonArray(consent.Consents.Select(EncodeConsent))
    };

    internal static AgentSchemaSamplingConsentSnapshot Decode(BsonDocument document, Guid principalId)
    {
        try
        {
            RequireFields(document, "_id", "schemaVersion", "revision", "updatedAtUtc", "consents");
            if (ReadGuid(document, "_id") != principalId ||
                !document["schemaVersion"].IsInt32 ||
                document["schemaVersion"].AsInt32 != AgentSchemaSamplingConsentSnapshot.CurrentSchemaVersion ||
                !document["revision"].IsInt64 || document["revision"].AsInt64 < 1 ||
                !document["updatedAtUtc"].IsDateTime ||
                !document["consents"].IsArray || document["consents"].AsArray.Count > MaximumConsents)
                throw InvalidConsent();
            var consents = document["consents"].AsArray.Select(value =>
                value.IsDocument ? DecodeConsent(value.AsDocument, principalId) : throw InvalidConsent()).ToArray();
            var snapshot = AgentSchemaSamplingConsentSnapshot.Load(
                principalId, document["schemaVersion"].AsInt32, document["revision"].AsInt64, consents);
            if (!snapshot.IsValid) throw InvalidConsent();
            return snapshot;
        }
        catch (ArgumentException)
        {
            // Do not include persisted content in diagnostics.
            throw InvalidConsent();
        }
    }

    private static BsonDocument EncodeConsent(AgentSchemaSamplingConsentGrant consent) => new()
    {
        ["principalId"] = consent.PrincipalId,
        ["sourceGenerationId"] = consent.SourceGenerationId,
        ["connectionId"] = consent.Scope.ConnectionId,
        ["databaseName"] = consent.Scope.DatabaseName is { } database ? new BsonValue(database) : BsonValue.Null,
        ["collectionName"] = consent.Scope.CollectionName is { } collection ? new BsonValue(collection) : BsonValue.Null,
        ["grantedAtUtc"] = consent.GrantedAtUtc.UtcDateTime,
        ["expiresAtUtc"] = consent.ExpiresAtUtc is { } expires ? new BsonValue(expires.UtcDateTime) : BsonValue.Null
    };

    private static AgentSchemaSamplingConsentGrant DecodeConsent(BsonDocument document, Guid principalId)
    {
        RequireFields(document, "principalId", "sourceGenerationId", "connectionId", "databaseName", "collectionName",
            "grantedAtUtc", "expiresAtUtc");
        if (ReadGuid(document, "principalId") != principalId) throw InvalidConsent();
        var connectionId = ReadGuid(document, "connectionId");
        var databaseName = ReadNullableString(document, "databaseName");
        var collectionName = ReadNullableString(document, "collectionName");
        var scope = (databaseName, collectionName) switch
        {
            (null, null) => AgentNamespaceScope.ForConnection(connectionId),
            (not null, null) => AgentNamespaceScope.ForDatabase(connectionId, databaseName),
            (not null, not null) => AgentNamespaceScope.ForCollection(connectionId, databaseName, collectionName),
            _ => throw InvalidConsent()
        };
        return new AgentSchemaSamplingConsentGrant(principalId, ReadGuid(document, "sourceGenerationId"), scope,
            ReadDateTime(document, "grantedAtUtc"), ReadNullableDateTime(document, "expiresAtUtc"));
    }

    private static void RequireFields(BsonDocument document, params string[] names)
    {
        if (document.Count != names.Length || !document.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(names))
            throw InvalidConsent();
    }

    private static Guid ReadGuid(BsonDocument document, string field) =>
        document[field].IsGuid && document[field].AsGuid != Guid.Empty ? document[field].AsGuid : throw InvalidConsent();

    private static string? ReadNullableString(BsonDocument document, string field) =>
        document[field].IsNull ? null : document[field].IsString ? document[field].AsString : throw InvalidConsent();

    private static DateTimeOffset ReadDateTime(BsonDocument document, string field) =>
        document[field].IsDateTime
            ? new DateTimeOffset(DateTime.SpecifyKind(document[field].AsDateTime, DateTimeKind.Utc))
            : throw InvalidConsent();

    private static DateTimeOffset? ReadNullableDateTime(BsonDocument document, string field) =>
        document[field].IsNull ? null : ReadDateTime(document, field);

    private static InvalidDataException InvalidConsent() =>
        new("Consentimento de amostragem de schema ilegível ou com versão não suportada. O documento foi preservado.");
}
