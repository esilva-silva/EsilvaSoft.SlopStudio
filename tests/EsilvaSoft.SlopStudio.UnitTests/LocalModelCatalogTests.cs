using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using static EsilvaSoft.SlopStudio.UnitTests.LocalModelFolderFixture;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class LocalModelCatalogTests
{
    private static readonly AiAccelerationMode[] CpuOnly = [AiAccelerationMode.Cpu];
    private static readonly string[] DiscoveredFolders = ["Broken-Metadata", "Empty", "Llama-Plain", "SlopCoder-Mongo-0.5B", "SlopCoder-Mongo-1.5B", "SlopCoder-Test"];

    [Test]
    public async Task DiscoveryUsesFolderNamesOptionalMetadataAndIsolatesInvalidFolders()
    {
        using var models = new TemporaryDirectory();
        CreateQwenModel(models.Path, "SlopCoder-Mongo-0.5B");
        CreateQwenModel(models.Path, "SlopCoder-Mongo-1.5B", """
            {"name":"SlopCoder Mongo 1.5B","version":"1.0.0","parameters":"1.5B","domain":["mongodb","json"],"capabilities":["autocomplete","fim","future"],
             "hardware":["cpu"],"recommendedContextTokens":2048,"recommendedCompletionTokens":128,"generation":{"chat":{"maxTokens":512,"temperature":0.3}}}
            """);
        File.Delete(Path.Combine(CreateQwenModel(models.Path, "SlopCoder-Test"), "tokenizer.json"));
        CreateQwenModel(models.Path, "Broken-Metadata", "{\"capabilities\":\"chat\"}");
        File.WriteAllText(Path.Combine(CreateQwenModel(models.Path, "Llama-Plain"), "genai_config.json"), "{\"model\":{\"type\":\"llama\",\"decoder\":{\"filename\":\"model.onnx\"}}}");
        Directory.CreateDirectory(Path.Combine(models.Path, "Empty"));

        var found = (await new LocalModelCatalog(models.Path).DiscoverAsync()).ToDictionary(validation => Path.GetFileName(validation.Path));

        Assert.That(found.Keys, Is.EqualTo(DiscoveredFolders));
        Assert.That(found["SlopCoder-Mongo-0.5B"].Model!.Name, Is.EqualTo("SlopCoder-Mongo-0.5B"));
        Assert.That(found["SlopCoder-Mongo-0.5B"].Model!.Capabilities.HasFlag(LocalModelCapabilities.Chat), Is.True);
        var described = found["SlopCoder-Mongo-1.5B"].Model!;
        Assert.That(described.Id, Is.EqualTo("SlopCoder-Mongo-1.5B"));
        Assert.That(described.Name, Is.EqualTo("SlopCoder Mongo 1.5B"));
        Assert.That(described.Capabilities, Is.EqualTo(LocalModelCapabilities.Autocomplete | LocalModelCapabilities.Fim));
        Assert.That(described.Metadata!.Hardware, Is.EqualTo(CpuOnly));
        Assert.That(described.Metadata.Chat, Is.EqualTo(new LocalModelGenerationDefaults(512, 0.3)));
        Assert.That(found["SlopCoder-Test"].Validity, Is.EqualTo(LocalModelValidity.MissingFiles));
        Assert.That(found["SlopCoder-Test"].Status.Message, Does.Contain("tokenizer.json"));
        Assert.That(found["Empty"].Validity, Is.EqualTo(LocalModelValidity.MissingFiles));
        Assert.That(found["Broken-Metadata"].Validity, Is.EqualTo(LocalModelValidity.Invalid));
        Assert.That(found["Llama-Plain"].Validity, Is.EqualTo(LocalModelValidity.Unsupported));
    }

    [TestCase("..")]
    [TestCase("models/other")]
    [TestCase(@"models\other")]
    [TestCase(@"C:\models\other")]
    [TestCase(" padded")]
    public void SelectedModelIsAFolderNameThatCannotEscapeTheDirectory(string name)
    {
        Assert.Throws<ArgumentException>(() => new AutocompleteSettings { SelectedModel = name }.Validate());
        Assert.Throws<ArgumentException>(() => new AutocompleteSettings { ChatModel = name }.Validate());
    }

    [Test]
    public void SelectionResolvesInsideTheDirectoryAndChatCanUseItsOwnModel()
    {
        var settings = new AutocompleteSettings { ModelDirectory = "models", SelectedModel = "Coder-0.5B", ChatModel = "Coder-1.5B", ModelPath = "external" }.Validate();
        Assert.That(settings.ResolveModelPath(LocalModelRole.Autocomplete, "default"), Is.EqualTo(Path.Combine("models", "Coder-0.5B")));
        Assert.That(settings.ResolveModelPath(LocalModelRole.Chat, "default"), Is.EqualTo(Path.Combine("models", "Coder-1.5B")));
        Assert.That((settings with { ModelDirectory = "" }).ResolveModelPath(LocalModelRole.Autocomplete, "default"), Is.EqualTo(Path.Combine("default", "Coder-0.5B")));
        Assert.That((settings with { SelectedModel = "", ChatModel = "" }).ResolveModelPath(LocalModelRole.Chat, "default"), Is.EqualTo("external"));
        Assert.That(new AutocompleteSettings().HasModelSelection(), Is.False);
    }
}
