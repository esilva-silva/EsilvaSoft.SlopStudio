using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Each immediate subfolder of the models directory is a candidate model. Validation is structural and never loads weights.</summary>
public sealed class LocalModelCatalog(string? defaultDirectory = null, IReadOnlyList<IModelAdapter>? adapters = null) : ILocalModelCatalog
{
    private const int MaximumCandidates = 100;
    private readonly IReadOnlyList<IModelAdapter> _adapters = adapters ?? ModelAdapters.Default;
    public string DefaultDirectory { get; } = defaultDirectory ?? LocalWorkspacePaths.GetModelsDirectory();

    public Task<IReadOnlyList<LocalModelValidation>> DiscoverAsync(CancellationToken cancellationToken = default) => DiscoverAsync(DefaultDirectory, cancellationToken);

    /// <summary>Invalid folders are returned with their state and never prevent the other models from being listed or loaded.</summary>
    public Task<IReadOnlyList<LocalModelValidation>> DiscoverAsync(string directory, CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<LocalModelValidation>>(() =>
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return [];
        return Directory.EnumerateDirectories(directory)
            .Where(path => !Path.GetFileName(path).StartsWith('.'))
            .Order(StringComparer.OrdinalIgnoreCase).Take(MaximumCandidates)
            .Select(path => Validate(path, cancellationToken)).ToArray();
    }, cancellationToken);

    public Task<LocalModelValidation> ValidateAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Validate(path, cancellationToken), cancellationToken);

    private LocalModelValidation Validate(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return new(null, new(LocalModelState.NotInstalled, "Modelo não instalado. Selecione um modelo do diretório ou uma pasta ONNX GenAI externa. Autocomplete básico ativo.")) { Path = path ?? "" };
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var folder = new DirectoryInfo(root).Name;
        LocalModelValidation Rejected(LocalModelState state, string message) => new(null, new(state, message) { ModelName = folder }) { Path = root };
        try
        {
            var configPath = Path.Combine(root, "genai_config.json");
            if (!File.Exists(configPath))
                return Rejected(LocalModelState.MissingFiles, "Arquivos ausentes: genai_config.json. Selecione a pasta de uma exportação ONNX Runtime GenAI.");
            using var config = ReadJson(configPath);
            var model = config.RootElement.GetProperty("model");
            var type = model.GetProperty("type").GetString() ?? "";
            var decoder = model.GetProperty("decoder").GetProperty("filename").GetString();
            if (string.IsNullOrWhiteSpace(decoder) || Path.IsPathRooted(decoder)) throw new InvalidDataException();
            var decoderPath = Path.GetFullPath(Path.Combine(root, decoder));
            var containedRoot = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
            if (!decoderPath.StartsWith(containedRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException();
            var missing = new[] { decoderPath, Path.Combine(root, "tokenizer.json"), Path.Combine(root, "tokenizer_config.json") }
                .Where(file => !File.Exists(file) || new FileInfo(file).Length == 0).Select(Path.GetFileName).ToArray();
            if (missing.Length > 0) return Rejected(LocalModelState.MissingFiles, "Arquivos ausentes ou vazios: " + string.Join(", ", missing) + ".");
            var adapter = _adapters.FirstOrDefault(candidate => candidate.CanHandle(type, root));
            if (adapter is null)
                return Rejected(LocalModelState.Unsupported, $"Arquitetura \"{type[..Math.Min(type.Length, 64)]}\" não suportada. Suportadas: {string.Join("; ", _adapters.Select(a => a.Description))}.");
            LocalModelMetadata? metadata;
            try { metadata = LocalModelMetadataReader.Read(root); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or FormatException)
            {
                return Rejected(LocalModelState.Invalid, $"{LocalModelMetadata.FileName} inválido: confira nomes, tipos e limites dos campos.");
            }
            using var tokenizer = ReadJson(Path.Combine(root, "tokenizer.json"));
            using (ReadJson(Path.Combine(root, "tokenizer_config.json"))) { }
            if (adapter.Validate(new(root, type, decoderPath, tokenizer.RootElement)) is { } failure) return Rejected(failure.State, failure.Message);
            token.ThrowIfCancellationRequested();
            var name = string.IsNullOrWhiteSpace(metadata?.Name) ? folder : metadata.Name;
            var definition = new LocalModelDefinition(folder, name, root, adapter.Architecture, Path.Combine(root, "tokenizer.json"))
            {
                PromptFormat = adapter.PromptFormat, Capabilities = metadata?.Capabilities ?? adapter.DefaultCapabilities, Metadata = metadata
            };
            return new(definition, new(LocalModelState.Available, "Arquivos encontrados. Use Testar modelo para validar inferência e provider.") { ModelName = name }) { Path = root };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            return Rejected(LocalModelState.Invalid, "Modelo inválido: confira genai_config.json, decoder ONNX, tokenizer.json e tokenizer_config.json.");
        }
    }

    internal static JsonDocument ReadJson(string path, long maximumBytes = 32 * 1024 * 1024)
    {
        if (new FileInfo(path).Length > maximumBytes) throw new InvalidDataException();
        using var stream = File.OpenRead(path);
        return JsonDocument.Parse(stream);
    }
}

/// <summary>Reads the optional slopstudio-model.json. Unknown capability or hardware names are ignored for forward compatibility.</summary>
internal static class LocalModelMetadataReader
{
    private static readonly Dictionary<string, LocalModelCapabilities> CapabilityNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["autocomplete"] = LocalModelCapabilities.Autocomplete, ["chat"] = LocalModelCapabilities.Chat,
        ["fim"] = LocalModelCapabilities.Fim, ["embeddings"] = LocalModelCapabilities.Embeddings, ["embedding"] = LocalModelCapabilities.Embeddings
    };

    private static readonly Dictionary<string, AiAccelerationMode> HardwareNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cpu"] = AiAccelerationMode.Cpu, ["gpu"] = AiAccelerationMode.Gpu, ["npu"] = AiAccelerationMode.Npu
    };

    public static LocalModelMetadata? Read(string root)
    {
        var path = Path.Combine(root, LocalModelMetadata.FileName);
        if (!File.Exists(path)) return null;
        using var document = LocalModelCatalog.ReadJson(path, 1024 * 1024);
        var json = document.RootElement;
        if (json.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        var generation = json.TryGetProperty("generation", out var value) && value.ValueKind != JsonValueKind.Null ? value : default;
        if (generation.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object)) throw new InvalidDataException();
        return new()
        {
            Name = Text(json, "name", 128),
            Version = Text(json, "version", 64),
            Architecture = Text(json, "architecture", 64),
            Parameters = Text(json, "parameters", 32),
            Domain = Texts(json, "domain")?.Where(item => item.Length is > 0 and <= 64).Take(32).ToArray() ?? [],
            Capabilities = Texts(json, "capabilities")?.Aggregate(LocalModelCapabilities.None,
                (current, item) => current | CapabilityNames.GetValueOrDefault(item, LocalModelCapabilities.None)),
            Hardware = Texts(json, "hardware")?.Where(HardwareNames.ContainsKey).Select(item => HardwareNames[item]).Distinct().ToArray(),
            RecommendedContextTokens = Integer(json, "recommendedContextTokens", 64, 8192),
            RecommendedCompletionTokens = Integer(json, "recommendedCompletionTokens", 1, 256),
            Autocomplete = Generation(generation, "autocomplete", 256),
            Chat = Generation(generation, "chat", 1024)
        };
    }

    private static string? Text(JsonElement json, string name, int maximumLength)
    {
        if (!json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException();
        var text = value.GetString()!.Trim();
        if (text.Length > maximumLength || text.Contains('\0')) throw new InvalidDataException();
        return text.Length == 0 ? null : text;
    }

    private static string[]? Texts(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException();
        return value.EnumerateArray().Take(64).Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()!.Trim() : throw new InvalidDataException()).ToArray();
    }

    private static int? Integer(JsonElement json, string name, int minimum, int maximum)
    {
        if (!json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= minimum && number <= maximum
            ? number : throw new InvalidDataException();
    }

    private static LocalModelGenerationDefaults Generation(JsonElement generation, string name, int maximumTokens)
    {
        if (generation.ValueKind != JsonValueKind.Object || !generation.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return new();
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        double? temperature = null;
        if (value.TryGetProperty("temperature", out var number) && number.ValueKind != JsonValueKind.Null)
            temperature = number.ValueKind == JsonValueKind.Number && number.TryGetDouble(out var parsed) && parsed is >= 0 and <= 2 ? parsed : throw new InvalidDataException();
        return new(Integer(value, "maxTokens", 1, maximumTokens), temperature);
    }
}
