using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EsilvaSoft.SlopStudio.Core;

public sealed record EnvironmentDefinition(string Name, Dictionary<string, string> Values);

public sealed record EnvironmentVault(int Version, string ActiveEnvironment, EnvironmentDefinition[] Environments)
{
    public static EnvironmentVault CreateDefault() => new(1, "Development",
        [new("Development", []), new("Staging", []), new("Production", [])]);

    public void Validate()
    {
        if (Version != 1 || Environments is null || Environments.Length == 0)
            throw new InvalidDataException("Cofre de ambientes inválido ou versão não suportada.");
        if (Environments.Any(e => e is null || string.IsNullOrWhiteSpace(e.Name) || e.Name.Length > 60 || e.Values is null)
            || Environments.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Environments.Length
            || !Environments.Any(e => e.Name == ActiveEnvironment))
            throw new InvalidDataException("Informe ambientes com nomes únicos e um ambiente ativo válido.");
        if (Environments.Any(e => e.Values.Any(v => string.IsNullOrWhiteSpace(v.Key) || v.Value is null)))
            throw new InvalidDataException("As chaves devem ter nome e os valores devem ser textos.");
    }

    public EnvironmentSnapshot Capture()
    {
        Validate();
        return new(ActiveEnvironment, Environments.Single(e => e.Name == ActiveEnvironment).Values);
    }
}

/// <summary>Immutable values captured before an operation starts; never persisted in workspace drafts.</summary>
public sealed class EnvironmentSnapshot(string name, IReadOnlyDictionary<string, string> values)
{
    public string Name { get; } = name;
    public IReadOnlyDictionary<string, string> Values { get; } = values.ToFrozenDictionary(StringComparer.Ordinal);
    public string Get(string key) => Values.TryGetValue(key, out var value) ? value
        : throw new InvalidOperationException($"Chave {key} não definida no ambiente {Name}.");
}

public static class DynamicValues
{
    private static readonly Regex Call = new("""ENV\.get\(\s*(?<quote>["'])(?<key>[^"'\r\n]+)\k<quote>\s*\)""", RegexOptions.CultureInvariant);
    private static readonly Regex Template = new("""\$\{ENV\.get\(\s*(?<quote>["'])(?<key>[^"'\r\n]+)\k<quote>\s*\)\}""", RegexOptions.CultureInvariant);

    public static string ResolveText(string text, Func<string, string> get, bool uriEncode = false) =>
        Template.Replace(text, m => uriEncode ? Uri.EscapeDataString(get(m.Groups["key"].Value)) : get(m.Groups["key"].Value));

    /// <summary>Replaces standalone calls with JSON string literals, leaving quoted text and BSON types intact.</summary>
    public static string ResolveJson(string text, Func<string, string> get)
    {
        var result = new StringBuilder();
        for (var i = 0; i < text.Length;)
        {
            if (text[i] is '"' or '\'')
            {
                var quote = text[i];
                result.Append(text[i++]);
                while (i < text.Length)
                {
                    var ch = text[i++]; result.Append(ch);
                    if (ch == '\\' && i < text.Length) result.Append(text[i++]);
                    else if (ch == quote) break;
                }
                continue;
            }
            var match = Call.Match(text, i);
            if (match.Success && match.Index == i && (i == 0 || !(char.IsLetterOrDigit(text[i - 1]) || text[i - 1] is '_' or '.')))
            {
                result.Append(JsonSerializer.Serialize(get(match.Groups["key"].Value)));
                i += match.Length;
            }
            else result.Append(text[i++]);
        }
        return result.ToString();
    }
}
