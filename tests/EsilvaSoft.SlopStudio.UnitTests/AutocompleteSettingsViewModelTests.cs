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

    [Test]
    public async Task UntouchedInlineFlagsStayAbsentAndFollowTheirLiveDefaults()
    {
        AutocompleteSettings? saved = null;
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), catalog: null, settings => { saved = settings; return Task.CompletedTask; });
        preferences.Load(new());

        // Nothing was touched: the effective values mirror the defaults, and no override is materialized.
        Assert.That((preferences.InlineEnabledEffective, preferences.InlineUseTraditionalEffective, preferences.InlineUseAiEffective), Is.EqualTo((true, true, false)));
        Assert.That((preferences.InlineEnabledIsOverridden, preferences.InlineUseTraditionalIsOverridden, preferences.InlineUseAiIsOverridden), Is.EqualTo((false, false, false)));

        // InlineUseTraditional keeps tracking UseDictionary live while it has no explicit override.
        preferences.UseDictionary = false;
        Assert.That(preferences.InlineUseTraditionalEffective, Is.False);
        Assert.That(preferences.InlineUseTraditionalIsOverridden, Is.False);

        await preferences.ApplyCommand.ExecuteAsync(null);
        Assert.That(saved!.InlineEnabledValue, Is.Null, "Never touched by the user: the field stays absent in the persisted document.");
        Assert.That(saved.InlineUseTraditionalValue, Is.Null);
        Assert.That(saved.InlineUseAiValue, Is.Null);
        // The effective (derived) values must still be correct for a fresh load elsewhere in the app.
        Assert.That((saved.InlineEnabled, saved.InlineUseTraditional, saved.InlineUseAi), Is.EqualTo((true, false, false)));
    }

    [Test]
    public async Task TogglingAnInlineFlagMaterializesAnExplicitValueAndResetClearsIt()
    {
        AutocompleteSettings? saved = null;
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), catalog: null, settings => { saved = settings; return Task.CompletedTask; });
        preferences.Load(new());

        preferences.InlineUseAiEffective = true;
        Assert.That(preferences.InlineUseAiIsOverridden, Is.True);
        await preferences.ApplyCommand.ExecuteAsync(null);
        Assert.That(saved!.InlineUseAiValue, Is.True, "A user toggle must persist as an explicit value, not just the effective default.");

        preferences.ResetInlineUseAiCommand.Execute(null);
        Assert.That(preferences.InlineUseAiIsOverridden, Is.False);
        Assert.That(preferences.InlineUseAiEffective, Is.False);
        await preferences.ApplyCommand.ExecuteAsync(null);
        Assert.That(saved.InlineUseAiValue, Is.Null, "Usar padrão must restore the absent state, not just flip the checkbox back to false.");

        // An explicit false must be distinguished from absence when reloaded (round-trip through Load/Snapshot).
        preferences.InlineEnabledEffective = false;
        var snapshot = preferences.Snapshot();
        Assert.That(snapshot.InlineEnabledValue, Is.False);
        preferences.Load(snapshot);
        Assert.That((preferences.InlineEnabledEffective, preferences.InlineEnabledIsOverridden), Is.EqualTo((false, true)));
    }

    [Test]
    public async Task CompletionListPreferencesRoundTrip()
    {
        AutocompleteSettings? saved = null;
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), catalog: null, settings => { saved = settings; return Task.CompletedTask; });
        preferences.Load(new() { CompletionAutoOpenOnTrigger = true, CompletionEnterAccepts = false });
        Assert.That((preferences.CompletionAutoOpenOnTrigger, preferences.CompletionEnterAccepts), Is.EqualTo((true, false)));
        await preferences.ApplyCommand.ExecuteAsync(null);
        Assert.That((saved!.CompletionAutoOpenOnTrigger, saved.CompletionEnterAccepts), Is.EqualTo((true, false)));
    }
}
