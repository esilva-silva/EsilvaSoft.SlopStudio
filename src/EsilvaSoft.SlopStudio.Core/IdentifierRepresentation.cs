using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EsilvaSoft.SlopStudio.Core;

/// <summary>
/// Default identifier of the IDE. Standard accepts and preserves both ObjectId and UUID; it is not a synonym of UUID v4.
/// The UUID byte order stays in the separate <see cref="UuidRepresentation"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IdentifierRepresentationMode>))]
public enum IdentifierRepresentationMode { Standard, ObjectId, UuidV4 }

public enum IdentifierKind { None, ObjectId, Uuid, UnknownLegacyUuid }

/// <summary>Mode and UUID representation resolved for one connection before an operation starts.</summary>
public readonly record struct IdentifierDisplayOptions(IdentifierRepresentationMode Mode, UuidRepresentation Uuid)
{
    public static IdentifierDisplayOptions Default => new(IdentifierRepresentationMode.Standard, UuidRepresentation.Standard);
}

/// <param name="Kind">Identifier type; for BSON input it is the stored type, never a guess from the text.</param>
/// <param name="Text">Text that restores the same BSON value, e.g. <c>ObjectId("…")</c> or <c>CGUUID("…")</c>.</param>
/// <param name="ExtendedJson">Canonical Extended JSON of the same value.</param>
/// <param name="UuidEquivalent">ObjectId only: deterministic alternative UUID text. It is presentation and is never written.</param>
/// <param name="IsExplicitType">True for BSON wrappers and named constructors; false when inferred from bare hexadecimal text.</param>
public sealed record IdentifierValue(IdentifierKind Kind, string Text, string ExtendedJson, string? UuidEquivalent, bool IsExplicitType);

public sealed record GeneratedIdentifier(IdentifierKind Kind, string Script, string ExtendedJson);

/// <summary>
/// Single place for identifier detection, parsing, formatting and the ObjectId → UUID alternative representation.
/// UUID bytes and subtypes are delegated to <see cref="UuidCodec"/>. Nothing here changes a stored BSON type.
/// </summary>
public static class IdentifierRepresentationService
{
    public const string SampleObjectId = "66e3bd5b3bc3f54c840d73ac";
    /// <summary>Well-formed UUID v4 (version 4, RFC 4122 variant) used as a script placeholder.</summary>
    public static Guid PlaceholderUuid { get; } = Guid.Parse("00000000-0000-4000-8000-000000000000");
    public const string PlaceholderObjectId = "000000000000000000000000";
    private const string ObjectIdHint = "Use 24 dígitos hexadecimais.";
    private static readonly byte[] ProcessUnique = RandomNumberGenerator.GetBytes(5);
    private static int _counter = RandomNumberGenerator.GetInt32(0x1000000);

    public static IReadOnlyList<IdentifierRepresentationMode> All { get; } =
        [IdentifierRepresentationMode.Standard, IdentifierRepresentationMode.ObjectId, IdentifierRepresentationMode.UuidV4];

    public static string DisplayName(IdentifierRepresentationMode mode) => mode switch
    {
        IdentifierRepresentationMode.Standard => "Standard",
        IdentifierRepresentationMode.ObjectId => "ObjectId",
        IdentifierRepresentationMode.UuidV4 => "UUID v4",
        _ => throw UnknownMode(mode)
    };

    public static string ChoiceLabel(IdentifierRepresentationMode mode) => mode switch
    {
        IdentifierRepresentationMode.Standard => "Standard · ObjectId + UUID v4",
        IdentifierRepresentationMode.ObjectId => "ObjectId · MongoDB ObjectId",
        IdentifierRepresentationMode.UuidV4 => "UUID v4 · BSON subtype 4",
        _ => throw UnknownMode(mode)
    };

    public static string Description(IdentifierRepresentationMode mode) => mode switch
    {
        IdentifierRepresentationMode.Standard => "Aceita ObjectId e UUID v4. A IDE preserva o tipo BSON original e utiliza a representação mais adequada para cada identificador.",
        IdentifierRepresentationMode.ObjectId => "Prioriza MongoDB ObjectId como representação padrão de identificadores. UUIDs existentes continuam UUID, na representação escolhida abaixo.",
        IdentifierRepresentationMode.UuidV4 => "Prioriza UUID padrão BSON subtype 4. ObjectIds podem ser representados como UUID quando a conversão for necessária, sem alterar o ObjectId gravado.",
        _ => throw UnknownMode(mode)
    };

    // ----- Detection -----

    /// <summary>Uses the BSON type expressed by the Extended JSON value; a JSON string is never an identifier.</summary>
    public static IdentifierKind DetectType(JsonElement value, UuidRepresentation uuid)
    {
        if (TryGetObjectId(value, out _)) return IdentifierKind.ObjectId;
        return UuidCodec.Describe(value, uuid).Kind switch
        {
            UuidDisplayKind.Uuid => IdentifierKind.Uuid,
            UuidDisplayKind.UnknownLegacy => IdentifierKind.UnknownLegacyUuid,
            _ => IdentifierKind.None
        };
    }

    /// <summary>Textual inference, used only when no BSON value is available.</summary>
    public static IdentifierKind DetectType(string text, IdentifierDisplayOptions options) =>
        TryParseIdentifier(text, options, out var value) ? value.Kind : IdentifierKind.None;

    public static bool IsObjectIdHex(ReadOnlySpan<char> text)
    {
        if (text.Length != 24) return false;
        foreach (var ch in text) if (!char.IsAsciiHexDigit(ch)) return false;
        return true;
    }

    public static bool TryGetObjectId(JsonElement value, out string hex)
    {
        hex = "";
        if (value.ValueKind != JsonValueKind.Object) return false;
        var count = 0;
        foreach (var property in value.EnumerateObject())
        {
            if (++count > 1 || !property.NameEquals("$oid") || property.Value.ValueKind != JsonValueKind.String) return false;
            hex = property.Value.GetString()!;
        }
        if (count == 1 && IsObjectIdHex(hex)) { hex = hex.ToLowerInvariant(); return true; }
        hex = "";
        return false;
    }

    // ----- Formatting and conversion -----

    public static string FormatObjectId(string hex) => "ObjectId(\"" + NormalizeObjectId(hex) + "\")";

    public static string ObjectIdToExtendedJson(string hex) => "{\"$oid\":\"" + NormalizeObjectId(hex) + "\"}";

    public static string FormatUuid(Guid value, UuidRepresentation representation) => UuidCodec.FormatConstructor(value, representation);

    /// <summary>
    /// Deterministic, reversible alternative: the 12 ObjectId bytes followed by 4 zero bytes, read in RFC 4122 order.
    /// The result is not a UUID v4 and is never written in place of the ObjectId.
    /// </summary>
    public static Guid ObjectIdToUuid(string hex)
    {
        Span<byte> bytes = stackalloc byte[16];
        Convert.FromHexString(NormalizeObjectId(hex)).CopyTo(bytes);
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>Inverse of <see cref="ObjectIdToUuid"/>; only UUIDs whose last four bytes are zero correspond to an ObjectId.</summary>
    public static bool TryUuidToObjectId(Guid value, out string hex)
    {
        var bytes = value.ToByteArray(bigEndian: true);
        hex = bytes.AsSpan(12).IndexOfAnyExcept((byte)0) < 0 ? Convert.ToHexStringLower(bytes, 0, 12) : "";
        return hex.Length > 0;
    }

    /// <summary>
    /// Human text of an ObjectId; UUID v4 mode appends the alternative UUID after the constructor, so the stored type stays first.
    /// The suffix is short enough for the 720 px value column of the result trees at the default code font.
    /// </summary>
    public static string DescribeObjectId(string hex, IdentifierDisplayOptions options) =>
        options.Mode == IdentifierRepresentationMode.UuidV4
            ? FormatObjectId(hex) + " · UUID " + ObjectIdToUuid(hex).ToString("D")
            : FormatObjectId(hex);

    /// <summary>Describes one Extended JSON value when it is an identifier; returns null for any other value.</summary>
    public static IdentifierValue? Describe(JsonElement value, IdentifierDisplayOptions options)
    {
        if (TryGetObjectId(value, out var hex))
            return new(IdentifierKind.ObjectId, FormatObjectId(hex), ObjectIdToExtendedJson(hex), ObjectIdToUuid(hex).ToString("D"), true);
        var uuid = UuidCodec.Describe(value, options.Uuid);
        return uuid.Kind switch
        {
            UuidDisplayKind.Uuid => new(IdentifierKind.Uuid, uuid.Text, RewriteConstructors(uuid.Text), null, true),
            UuidDisplayKind.UnknownLegacy => new(IdentifierKind.UnknownLegacyUuid, value.GetRawText(), value.GetRawText(), null, true),
            _ => null
        };
    }

    // ----- Parsing -----

    /// <summary>
    /// Accepts <c>ObjectId("…")</c>, 24 hexadecimal digits, <c>UUID("…")</c>/<c>CGUUID</c>/<c>JUUID</c>/<c>GUUID</c>,
    /// a UUID with 32 digits (grouped or not) and canonical <c>$oid</c>/<c>$binary</c> wrappers. Wrappers and constructors
    /// carry the type explicitly and take priority; bare UUID text uses the configured UUID representation.
    /// The mode never rejects a valid explicit identifier, so existing data remains reachable in every mode.
    /// </summary>
    public static IdentifierValue ParseIdentifier(string text, IdentifierDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!Enum.IsDefined(options.Mode)) throw UnknownMode(options.Mode);
        var trimmed = text.Trim();
        if (trimmed.StartsWith('{') || trimmed.Contains('(', StringComparison.Ordinal))
        {
            // Invalid constructor arguments keep the precise message and location from the rewrite.
            var canonical = trimmed.StartsWith('{') ? trimmed : RewriteConstructors(trimmed);
            try
            {
                using var document = JsonDocument.Parse(canonical);
                if (Describe(document.RootElement, options) is { } described) return described;
            }
            catch (JsonException) { }
            throw Unrecognized(trimmed);
        }
        if (IsObjectIdHex(trimmed))
        {
            var hex = trimmed.ToLowerInvariant();
            return new(IdentifierKind.ObjectId, FormatObjectId(hex), ObjectIdToExtendedJson(hex), ObjectIdToUuid(hex).ToString("D"), false);
        }
        if ((trimmed.Length == 36 && Guid.TryParseExact(trimmed, "D", out var uuid)) || (trimmed.Length == 32 && Guid.TryParseExact(trimmed, "N", out uuid)))
            return new(IdentifierKind.Uuid, FormatUuid(uuid, options.Uuid), UuidCodec.ToExtendedJson(uuid, options.Uuid), null, false);
        throw Unrecognized(trimmed);
    }

    public static bool TryParseIdentifier(string text, IdentifierDisplayOptions options, out IdentifierValue value)
    {
        try { value = ParseIdentifier(text, options); return true; }
        catch (FormatException) { value = null!; return false; }
    }

    // ----- Generation and scripts -----

    /// <summary>New ObjectId: 4-byte big-endian seconds, 5 process-unique random bytes and a 3-byte counter.</summary>
    public static string NewObjectId(DateTimeOffset? now = null)
    {
        Span<byte> bytes = stackalloc byte[12];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)(now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds());
        ProcessUnique.CopyTo(bytes[4..]);
        var counter = Interlocked.Increment(ref _counter) & 0xFFFFFF;
        bytes[9] = (byte)(counter >> 16); bytes[10] = (byte)(counter >> 8); bytes[11] = (byte)counter;
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>Random UUID v4 (version 4, RFC 4122 variant).</summary>
    public static Guid NewUuidV4() => Guid.NewGuid();

    /// <summary>ObjectId mode generates ObjectId, UUID v4 mode a UUID v4 in the configured representation, Standard both.</summary>
    public static IReadOnlyList<GeneratedIdentifier> Generate(IdentifierDisplayOptions options)
    {
        var list = new List<GeneratedIdentifier>(2);
        if (options.Mode is IdentifierRepresentationMode.Standard or IdentifierRepresentationMode.ObjectId)
        {
            var hex = NewObjectId();
            list.Add(new(IdentifierKind.ObjectId, FormatObjectId(hex), ObjectIdToExtendedJson(hex)));
        }
        if (options.Mode is IdentifierRepresentationMode.Standard or IdentifierRepresentationMode.UuidV4)
        {
            var uuid = NewUuidV4();
            list.Add(new(IdentifierKind.Uuid, FormatUuid(uuid, options.Uuid), UuidCodec.ToExtendedJson(uuid, options.Uuid)));
        }
        if (list.Count == 0) throw UnknownMode(options.Mode);
        return list;
    }

    /// <summary>Placeholder <c>_id</c> of generated CRUD scripts. Standard shows the ObjectId and, in a comment, the UUID alternative.</summary>
    public static string ScriptIdentifierPlaceholder(IdentifierDisplayOptions options) => options.Mode switch
    {
        IdentifierRepresentationMode.ObjectId => FormatObjectId(PlaceholderObjectId),
        IdentifierRepresentationMode.UuidV4 => FormatUuid(PlaceholderUuid, options.Uuid),
        IdentifierRepresentationMode.Standard => FormatObjectId(PlaceholderObjectId) + " /* ou " + FormatUuid(PlaceholderUuid, options.Uuid) + " */",
        _ => throw UnknownMode(options.Mode)
    };

    // ----- Human text <-> canonical Extended JSON -----

    /// <summary>
    /// Replaces canonical Date, ObjectId and UUID wrappers by shell constructors, byte-for-byte preserving the rest of the text.
    /// The result goes back to identical BSON through <see cref="RewriteConstructors"/>.
    /// </summary>
    public static UuidDisplayText FormatForDisplay(string json, IdentifierDisplayOptions options) => Format(json, options.Uuid, objectIds: true);

    /// <summary>Converts Date("…"), ISODate("…"), ObjectId("…") and the UUID constructors written outside strings and comments into Canonical Extended JSON.</summary>
    public static string RewriteConstructors(string text) => Rewrite(text, objectIds: true);

    internal static UuidDisplayText Format(string json, UuidRepresentation representation, bool objectIds)
    {
        if (string.IsNullOrEmpty(json) || !(json.Contains("$binary", StringComparison.Ordinal) || (objectIds && (json.Contains("$oid", StringComparison.Ordinal) || json.Contains("$date", StringComparison.Ordinal)))))
            return new(json, 0);
        var utf8 = Encoding.UTF8.GetBytes(json);
        var output = new StringBuilder(json.Length);
        var copied = 0;
        var unknown = 0;
        try
        {
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { MaxDepth = 512 });
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.StartObject) continue;
                var start = (int)reader.TokenStartIndex;
                var probe = reader;
                string? replacement = null;
                if (objectIds && TryReadDate(ref probe, out var date)) replacement = date;
                else if (objectIds && TryReadObjectId(ref probe, out var hex)) replacement = FormatObjectId(hex);
                else
                {
                    probe = reader;
                    if (!TryReadBinary(ref probe, out var base64, out var subType)) continue;
                    var value = UuidCodec.Describe(base64, subType, representation);
                    if (value.Kind == UuidDisplayKind.UnknownLegacy) unknown++;
                    if (value.Kind == UuidDisplayKind.Uuid) replacement = value.Text;
                }
                reader = probe;
                if (replacement is null) continue;
                output.Append(Encoding.UTF8.GetString(utf8, copied, start - copied)).Append(replacement);
                copied = (int)reader.BytesConsumed;
            }
        }
        catch (JsonException)
        {
            return new(json, 0);
        }
        if (copied == 0) return new(json, unknown);
        output.Append(Encoding.UTF8.GetString(utf8, copied, utf8.Length - copied));
        return new(output.ToString(), unknown);
    }

    internal static string Rewrite(string text, bool objectIds)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Contains("UUID", StringComparison.Ordinal) && !(objectIds && (text.Contains("ObjectId", StringComparison.Ordinal) || text.Contains("Date", StringComparison.Ordinal)))) return text;
        StringBuilder? result = null;
        var copied = 0;
        for (var i = 0; i < text.Length;)
        {
            var ch = text[i];
            if (ch is '"' or '\'' or '`') { i = SkipQuoted(text, i); continue; }
            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '/') { var line = text.IndexOf('\n', i); i = line < 0 ? text.Length : line; continue; }
            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '*') { var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal); i = close < 0 ? text.Length : close + 2; continue; }
            if (ch == '/') { i = SkipRegex(text, i); continue; }
            if (!IsIdentifierStart(ch)) { i++; continue; }
            var end = i + 1;
            while (end < text.Length && IsIdentifierPart(text[end])) end++;
            var name = text[i..end];
            var memberAccess = i > 0 && text[i - 1] == '.';
            var isObjectId = objectIds && name is "ObjectId" or "Date" or "ISODate";
            if (!memberAccess && (isObjectId || UuidCodec.TryParseConstructor(name, out _)))
            {
                var open = SkipWhitespace(text, end);
                if (open < text.Length && text[open] == '(')
                {
                    var replacement = ReadCall(text, i, name, open, out var callEnd);
                    // "new ObjectId(…)" and "new UUID(…)" are accepted by shell-mode JSON; the keyword goes with the call.
                    var start = NewKeywordStart(text, i, copied);
                    (result ??= new StringBuilder(text.Length)).Append(text, copied, start - copied).Append(replacement);
                    copied = callEnd;
                    i = callEnd;
                    continue;
                }
            }
            i = end;
        }
        return result is null ? text : result.Append(text, copied, text.Length - copied).ToString();
    }

    private static string ReadCall(string text, int start, string name, int open, out int end)
    {
        var cursor = SkipWhitespace(text, open + 1);
        if (cursor >= text.Length || text[cursor] is not ('"' or '\'')) throw CallError(text, start, name);
        var quote = text[cursor];
        var close = text.IndexOf(quote, cursor + 1);
        if (close < 0) throw CallError(text, start, name);
        var value = text[(cursor + 1)..close];
        cursor = SkipWhitespace(text, close + 1);
        if (cursor >= text.Length || text[cursor] != ')' || value.Contains('\\', StringComparison.Ordinal)) throw CallError(text, start, name);
        end = cursor + 1;
        if (name is "Date" or "ISODate") return BsonDatePresentation.ToExtendedJson(value);
        if (name == "ObjectId")
        {
            return IsObjectIdHex(value)
                ? ObjectIdToExtendedJson(value)
                : throw new FormatException($"ObjectId(\"{Shorten(value)}\") não contém um ObjectId válido. {ObjectIdHint} {Location(text, start)}");
        }
        if (!UuidCodec.TryParseConstructor(name, out var representation)) throw CallError(text, start, name);
        try
        {
            return UuidCodec.ToExtendedJson(UuidCodec.ParseText(name, value), representation);
        }
        catch (FormatException exception)
        {
            throw new FormatException(exception.Message + " " + Location(text, start), exception);
        }
    }

    private static int NewKeywordStart(string text, int constructorStart, int copied)
    {
        var cursor = constructorStart;
        while (cursor > copied && char.IsWhiteSpace(text[cursor - 1])) cursor--;
        if (cursor == constructorStart || cursor - 3 < copied || !text.AsSpan(cursor - 3, 3).SequenceEqual("new")) return constructorStart;
        return cursor - 3 > 0 && IsIdentifierPart(text[cursor - 4]) ? constructorStart : cursor - 3;
    }

    private static bool TryReadDate(ref Utf8JsonReader reader, out string? literal)
    {
        var probe = reader;
        literal = null;
        if (!probe.Read() || probe.TokenType != JsonTokenType.PropertyName || !probe.ValueTextEquals("$date")) return false;
        probe = reader;
        using var document = JsonDocument.ParseValue(ref probe);
        literal = BsonDatePresentation.Format(document.RootElement);
        if (literal is null) return false;
        reader = probe;
        return true;
    }

    private static bool TryReadObjectId(ref Utf8JsonReader reader, out string hex)
    {
        hex = "";
        if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("$oid")) return false;
        if (!reader.Read() || reader.TokenType != JsonTokenType.String) return false;
        var value = reader.GetString()!;
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject || !IsObjectIdHex(value)) return false;
        hex = value;
        return true;
    }

    private static bool TryReadBinary(ref Utf8JsonReader reader, out string base64, out string subType)
    {
        base64 = subType = "";
        if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("$binary")) return false;
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;
        string? data = null, kind = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isData = reader.ValueTextEquals("base64");
            var isKind = reader.ValueTextEquals("subType");
            if (!reader.Read() || reader.TokenType != JsonTokenType.String || (isData ? data is not null : !isKind || kind is not null)) return false;
            if (isData) data = reader.GetString(); else kind = reader.GetString();
        }
        if (reader.TokenType != JsonTokenType.EndObject || data is null || kind is null) return false;
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject) return false;
        base64 = data; subType = kind;
        return true;
    }

    private static string NormalizeObjectId(string hex) =>
        IsObjectIdHex(hex) ? hex.ToLowerInvariant() : throw new FormatException($"\"{Shorten(hex)}\" não é um ObjectId válido. {ObjectIdHint}");

    private static FormatException Unrecognized(string text) =>
        new($"\"{Shorten(text)}\" não é um identificador reconhecido. Use ObjectId(\"…\") ou 24 dígitos hexadecimais, UUID(\"…\")/CGUUID/JUUID/GUUID ou um UUID com 32 dígitos, com ou sem hífens.");

    private static FormatException CallError(string text, int start, string name) =>
        new($"{name}(...) exige um único texto entre aspas. {(name == "ObjectId" ? ObjectIdHint : "Use 32 dígitos hexadecimais, com ou sem hífens.")} {Location(text, start)}");

    private static ArgumentOutOfRangeException UnknownMode(IdentifierRepresentationMode mode) =>
        new(nameof(mode), mode, "Modo de identificador desconhecido.");

    private static string Location(string text, int index)
    {
        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < index; i++)
        {
            if (text[i] == '\n') { line++; lineStart = i + 1; }
        }
        return $"(linha {line}, coluna {index - lineStart + 1})";
    }

    private static int SkipQuoted(string text, int start)
    {
        var quote = text[start];
        for (var i = start + 1; i < text.Length; i++)
        {
            if (text[i] == '\\') i++;
            else if (text[i] == quote) return i + 1;
        }
        return text.Length;
    }

    /// <summary>Skips a /pattern/flags literal on one line; a slash without closing delimiter is ordinary text.</summary>
    private static int SkipRegex(string text, int start)
    {
        var inClass = false;
        for (var i = start + 1; i < text.Length && text[i] is not ('\n' or '\r'); i++)
        {
            if (text[i] == '\\') i++;
            else if (text[i] == '[') inClass = true;
            else if (text[i] == ']') inClass = false;
            else if (text[i] == '/' && !inClass) return i + 1;
        }
        return start + 1;
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index;
    }

    private static bool IsIdentifierStart(char ch) => char.IsLetter(ch) || ch is '_' or '$';
    private static bool IsIdentifierPart(char ch) => char.IsLetterOrDigit(ch) || ch is '_' or '$';
    private static string Shorten(string value) => value.Length <= 60 ? value : value[..60] + "…";
}
