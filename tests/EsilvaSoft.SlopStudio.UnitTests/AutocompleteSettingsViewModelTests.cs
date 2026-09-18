using EsilvaSoft.SlopStudio.Infrastructure.LocalAi;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using EsilvaSoft.SlopStudio.Infrastructure;
using static EsilvaSoft.SlopStudio.UnitTests.LocalModelFolderFixture;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class AutocompleteSettingsViewModelTests
{
    private static readonly string[] TwoModels = ["Coder-0.5B", "Coder 1.5B"];
    private static readonly string[] RefreshedModels = ["Coder 1.5B", "Coder-3B"];

    [Test]
    public async Task PreferencesStoreFolderNamesAndRefreshKeepsTheSelection()
    {
        using var models = new TemporaryDirectory();
        CreateQwenModel(models.Path, "Coder-0.5B");
        CreateQwenModel(models.Path, "Coder-1.5B", "{\"name\":\"Coder 1.5B\",\"recommendedContextTokens\":1024,\"recommendedCompletionTokens\":64}");
        AutocompleteSettings? saved = null;
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), new LocalModelCatalog(models.Path), settings => { saved = settings; return Task.CompletedTask; });
        preferences.Load(new());
        await preferences.RefreshModelsCommand.ExecuteAsync(null);
        Assert.That(preferences.Models.Select(option => option.Display), Is.EqualTo(TwoModels));
        preferences.SelectedModelOption = preferences.Models.Single(option => option.Reference == "Coder-1.5B");
        Assert.That((preferences.ContextTokens, preferences.MaximumTokens), Is.EqualTo((1024, 64)));
        await preferences.ApplyCommand.ExecuteAsync(null);
        Assert.That((saved!.SelectedModel, saved.ModelPath, saved.ModelDirectory), Is.EqualTo(("Coder-1.5B", "", "")));

        Directory.Delete(Path.Combine(models.Path, "Coder-0.5B"), true);
        CreateQwenModel(models.Path, "Coder-3B");
        await preferences.RefreshModelsCommand.ExecuteAsync(null);
        Assert.That(preferences.Models.Select(option => option.Display), Is.EqualTo(RefreshedModels));
        Assert.That(preferences.SelectedModelOption!.Reference, Is.EqualTo("Coder-1.5B"));
        Assert.That(preferences.ContextTokens, Is.EqualTo(1024));

        await preferences.SelectExternalModelAsync(Path.Combine(models.Path, "Coder-3B"));
        await preferences.ApplyCommand.ExecuteAsync(null);
        Assert.That((saved.SelectedModel, saved.ModelPath), Is.EqualTo(("Coder-3B", "")), "A folder inside the directory is not stored as an absolute path.");
        preferences.Load(saved);
        Assert.That(preferences.SelectedModelOption!.Reference, Is.EqualTo("Coder-3B"));
    }
}
