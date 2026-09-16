using System.Text.Json;
using System.Text.Json.Nodes;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class EditorKeyBindingsTests
{
    // Independent fixture written by hand from the shortcut table, not derived from EditorKeyBindings.Defaults.
    private static readonly Dictionary<string, string[]> ExpectedDefaults = new()
    {
        ["editor.completion.show"] = ["Ctrl+.", "Ctrl+Space"],
        ["editor.completion.ai"] = ["Ctrl+;"],
        ["editor.inline.accept"] = ["Tab"],
        ["editor.inline.dismiss"] = ["Escape"]
    };

    [Test]
    public void DefaultsBindTheFourEditorCommandsAndListFlagsKeepConservativeValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Texts(EditorKeyBindings.Resolve(null)), Is.EqualTo(ExpectedDefaults));
            Assert.That(Texts(EditorKeyBindings.Resolve(new EditorKeyBindings())), Is.EqualTo(ExpectedDefaults), "Sem entradas, cada comando mantém o padrão.");
            Assert.That(new WorkspacePreferences().EditorKeyBindings, Is.Null);
            Assert.That((new AutocompleteSettings().CompletionAutoOpenOnTrigger, new AutocompleteSettings().CompletionEnterAccepts), Is.EqualTo((false, true)));
            Assert.That(JsonSerializer.Serialize(new WorkspacePreferences()), Does.Not.Contain("EditorKeyBindings"), "Ausência não é materializada ao gravar.");
        });
    }

    [TestCase("Ctrl+.", EditorKeyModifiers.Control, null, ".", "Ctrl+.")]
    [TestCase("Ctrl+;", EditorKeyModifiers.Control, null, ";", "Ctrl+;")]
    [TestCase("Ctrl+Space", EditorKeyModifiers.Control, "Space", null, "Ctrl+Space")]
    [TestCase("Shift+Tab", EditorKeyModifiers.Shift, "Tab", null, "Shift+Tab")]
    [TestCase("Escape", EditorKeyModifiers.None, "Escape", null, "Escape")]
    [TestCase("shift+ctrl+a", EditorKeyModifiers.Control | EditorKeyModifiers.Shift, "A", null, "Ctrl+Shift+A")]
    [TestCase("Ctrl++", EditorKeyModifiers.Control, null, "+", "Ctrl++")]
    [TestCase("Alt+]", EditorKeyModifiers.Alt, null, "]", "Alt+]")]
    [TestCase("Meta+0", EditorKeyModifiers.Meta, "D0", null, "Meta+0")]
    [TestCase("F5", EditorKeyModifiers.None, "F5", null, "F5")]
    public void GestureTextParsesToModifiersPlusNamedKeyOrSymbol(string text, EditorKeyModifiers modifiers, string? key, string? symbol, string canonical)
    {
        Assert.That(EditorKeyGesture.TryParse(text, out var gesture, out var error), Is.True, error);
        Assert.Multiple(() =>
        {
            Assert.That(gesture.Modifiers, Is.EqualTo(modifiers));
            Assert.That(gesture.Key?.ToString(), Is.EqualTo(key));
            Assert.That(gesture.Symbol?.ToString(), Is.EqualTo(symbol));
            Assert.That(gesture.ToString(), Is.EqualTo(canonical));
            Assert.That(EditorKeyGesture.Parse(canonical), Is.EqualTo(gesture), "O texto canônico volta ao mesmo gesto.");
        });
    }

    [Test]
    public void EquivalentGesturesAreEqualAndFactoriesApplyTheSameRules()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EditorKeyGesture.Parse("Shift+Ctrl+."), Is.EqualTo(EditorKeyGesture.Parse("Ctrl+Shift+.")));
            Assert.That(EditorKeyGesture.ForSymbol(';', EditorKeyModifiers.Control), Is.EqualTo(EditorKeyGesture.Parse("Ctrl+;")));
            Assert.That(EditorKeyGesture.ForKey(EditorKey.Space, EditorKeyModifiers.Control), Is.EqualTo(EditorKeyGesture.Parse("ctrl+space")));
            Assert.That(EditorKeyGesture.Parse("Ctrl+."), Is.Not.EqualTo(EditorKeyGesture.Parse("Ctrl+;")));
            Assert.Throws<ArgumentException>(() => EditorKeyGesture.ForSymbol('a', EditorKeyModifiers.Control));
            Assert.Throws<ArgumentException>(() => EditorKeyGesture.ForSymbol('.'));
            Assert.Throws<ArgumentException>(() => EditorKeyGesture.ForKey(EditorKey.A, EditorKeyModifiers.Shift));
            Assert.Throws<ArgumentException>(() => EditorKeyGesture.ForKey((EditorKey)999, EditorKeyModifiers.Control));
            Assert.That(default(EditorKeyGesture).ToString(), Is.Empty);
        });
    }

    [Test]
    public void DispatcherUsesProducedSymbolBeforePhysicalFallbackAndHonorsCustomBindings()
    {
        var defaults = new EditorCommandDispatcher(EditorKeyBindings.Resolve(null));
        var custom = new EditorCommandDispatcher(EditorKeyBindings.Resolve(new EditorKeyBindings
        {
            Bindings = new() { [EditorCommandIds.CompletionShow] = ["Ctrl+Shift+Space"] }
        }));

        Assert.Multiple(() =>
        {
            Assert.That(defaults.Match(new(EditorKeyModifiers.Control, '.', EditorKey.Space, ';')), Is.EqualTo(EditorCommandIds.CompletionShow), "O símbolo produzido funciona em layouts não-US.");
            Assert.That(defaults.Match(new(EditorKeyModifiers.Control, null, EditorKey.Space)), Is.EqualTo(EditorCommandIds.CompletionShow), "Sem símbolo, a tecla física é o fallback.");
            Assert.That(defaults.Match(new(EditorKeyModifiers.Control, ':', EditorKey.Space, ';')), Is.Null, "Símbolo presente e diferente bloqueia o fallback físico.");
            Assert.That(defaults.Match(new(EditorKeyModifiers.None, null, EditorKey.Tab)), Is.EqualTo(EditorCommandIds.InlineAccept));
            Assert.That(custom.Match(new(EditorKeyModifiers.Control | EditorKeyModifiers.Shift, null, EditorKey.Space)), Is.EqualTo(EditorCommandIds.CompletionShow));
            Assert.That(custom.Match(new(EditorKeyModifiers.Control, '.', EditorKey.Space, '.')), Is.Null, "A preferência substitui, em vez de complementar, o padrão do comando.");
        });
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("Ctrl+")]
    [TestCase("+")]
    [TestCase("+Ctrl")]
    [TestCase("Ctrl + .")]
    [TestCase("Ctrl+Shift")]
    [TestCase("Ctrl+Ctrl+.")]
    [TestCase("Ctrl++Shift+.")]
    [TestCase("Hyper+.")]
    [TestCase("Ctrl+Esc")]
    [TestCase("Ctrl+..")]
    [TestCase("Ctrl+D0")]
    [TestCase("Ctrl+ç")]
    [TestCase(".")]
    [TestCase("A")]
    [TestCase("Space")]
    [TestCase("Shift+;")]
    public void MalformedGestureTextIsRejectedWithAReason(string text)
    {
        Assert.Multiple(() =>
        {
            Assert.That(EditorKeyGesture.TryParse(text, out var gesture, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(gesture, Is.EqualTo(default(EditorKeyGesture)));
            Assert.Throws<FormatException>(() => EditorKeyGesture.Parse(text));
        });
    }

    [Test]
    public void NullGestureTextIsRejected() =>
        Assert.That(EditorKeyGesture.TryParse(null, out _, out var error) ? null : error, Is.EqualTo("Gesto vazio."));

    [Test]
    public async Task OlderSessionWithoutShortcutsOrListFlagsLoadsDefaultsAndSavesWithoutMaterializingThem()
    {
        var path = NewDatabasePath();
        var original = SessionJson();
        Assert.That(original, Does.Not.Contain("EditorKeyBindings").And.Not.Contain("CompletionAutoOpenOnTrigger").And.Not.Contain("CompletionEnterAccepts"));
        WriteRawSession(path, original);
        using var context = new WorkspaceTestContext();
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            var loaded = (await repository.LoadSessionAsync()).Preferences;
            Assert.Multiple(() =>
            {
                Assert.That(loaded.EditorKeyBindings, Is.Null);
                Assert.That((loaded.Autocomplete.CompletionAutoOpenOnTrigger, loaded.Autocomplete.CompletionEnterAccepts), Is.EqualTo((false, true)));
                Assert.That(loaded.Theme, Is.EqualTo("Escuro"));
            });
            using var workspace = new WorkspaceViewModel(context.Workspace, repository);
            await workspace.InitializeAsync();
            Assert.That(Texts(workspace.KeyBindings), Is.EqualTo(ExpectedDefaults));
            await workspace.SetUuidRepresentationAsync(UuidRepresentation.JavaLegacy);
        }
        Assert.That(ReadRawSession(path), Does.Not.Contain("EditorKeyBindings").And.Contain("\"UuidRepresentation\":\"JavaLegacy\"")
            .And.Contain("\"CompletionAutoOpenOnTrigger\":false").And.Contain("\"CompletionEnterAccepts\":true").And.Contain("\"Theme\":\"Escuro\""));
        using (var repository = new LiteDbConnectionProfileRepository(path))
            Assert.That((await repository.LoadSessionAsync()).Preferences.EditorKeyBindings, Is.Null);
    }

    [TestCase(true, false)]
    [TestCase(false, false)]
    public async Task ExplicitListFlagsSurviveLoadPreferencesApplyAndReload(bool autoOpen, bool enterAccepts)
    {
        var path = NewDatabasePath();
        WriteRawSession(path, SessionJson(preferences =>
        {
            preferences["Autocomplete"]!["CompletionAutoOpenOnTrigger"] = autoOpen;
            preferences["Autocomplete"]!["CompletionEnterAccepts"] = enterAccepts;
        }));
        using var context = new WorkspaceTestContext();
        var service = new CompletionServiceFake();
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            var loaded = (await repository.LoadSessionAsync()).Preferences.Autocomplete;
            Assert.That((loaded.CompletionAutoOpenOnTrigger, loaded.CompletionEnterAccepts), Is.EqualTo((autoOpen, enterAccepts)));
            using var workspace = new WorkspaceViewModel(context.Workspace, repository, service);
            await workspace.InitializeAsync();
            // Saving from the preferences window rebuilds the settings; fields without a control must keep their values.
            workspace.AutocompletePreferences.DelayMilliseconds = 300;
            await workspace.AutocompletePreferences.ApplyCommand.ExecuteAsync(null);
            Assert.That(workspace.AutocompletePreferences.OperationStatus, Is.EqualTo("Preferências de autocomplete salvas."));
            Assert.That((service.Settings.CompletionAutoOpenOnTrigger, service.Settings.CompletionEnterAccepts, service.Settings.DelayMilliseconds), Is.EqualTo((autoOpen, enterAccepts, 300)));
        }
        Assert.That(ReadRawSession(path), Does.Contain($"\"CompletionAutoOpenOnTrigger\":{(autoOpen ? "true" : "false")}")
            .And.Contain("\"CompletionEnterAccepts\":false").And.Contain("\"DelayMilliseconds\":300"));
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            var restored = (await repository.LoadSessionAsync()).Preferences.Autocomplete;
            Assert.That((restored.CompletionAutoOpenOnTrigger, restored.CompletionEnterAccepts, restored.DelayMilliseconds), Is.EqualTo((autoOpen, enterAccepts, 300)));
        }
    }

    [Test]
    public async Task CustomShortcutsRoundTripThroughTheRepositoryAndWorkspaceSaves()
    {
        var path = NewDatabasePath();
        using var context = new WorkspaceTestContext();
        var custom = new Dictionary<string, string[]> { ["editor.completion.show"] = ["Alt+.", "Ctrl+Space"], ["editor.inline.accept"] = [] };
        var expected = new Dictionary<string, string[]>
        {
            ["editor.completion.show"] = ["Alt+.", "Ctrl+Space"],
            ["editor.completion.ai"] = ["Ctrl+;"],
            ["editor.inline.accept"] = [],
            ["editor.inline.dismiss"] = ["Escape"]
        };
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            await repository.SaveSessionAsync(new WorkspaceSession { Preferences = new() { Theme = "Escuro", EditorKeyBindings = new() { Bindings = custom } } });
            using var workspace = new WorkspaceViewModel(context.Workspace, repository);
            await workspace.InitializeAsync();
            Assert.That(Texts(workspace.KeyBindings), Is.EqualTo(expected));
            await workspace.SetUuidRepresentationAsync(UuidRepresentation.JavaLegacy);
        }
        var raw = JsonNode.Parse(ReadRawSession(path))!["Preferences"]!;
        Assert.Multiple(() =>
        {
            Assert.That(raw["UuidRepresentation"]!.GetValue<string>(), Is.EqualTo("JavaLegacy"), "A gravação pela área de trabalho ocorreu.");
            Assert.That(raw["EditorKeyBindings"]!["Version"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(raw["EditorKeyBindings"]!["Bindings"]!.AsObject().ToDictionary(entry => entry.Key, entry => entry.Value!.AsArray().Select(item => item!.GetValue<string>()).ToArray()),
                Is.EqualTo(custom), "Somente as entradas personalizadas são gravadas; padrões não são materializados.");
        });
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            var restored = (await repository.LoadSessionAsync()).Preferences.EditorKeyBindings;
            Assert.That(restored!.Bindings, Is.EqualTo(custom));
            Assert.That(Texts(EditorKeyBindings.Resolve(restored)), Is.EqualTo(expected));
        }
    }

    private static IEnumerable<TestCaseData> InvalidPreferences()
    {
        static TestCaseData Shortcut(string name, string json, string? reason) =>
            new TestCaseData("EditorKeyBindings", json, reason).SetName("InvalidPreferenceKeepsSession_" + name);
        yield return Shortcut("UnknownCommand", "{\"Version\":1,\"Bindings\":{\"editor.completion.unknown\":[\"Ctrl+.\"]}}", "comando 'editor.completion.unknown' desconhecido");
        yield return Shortcut("GestureWithoutKey", "{\"Version\":1,\"Bindings\":{\"editor.completion.show\":[\"Ctrl+\"]}}", "Gesto 'Ctrl+' sem tecla");
        yield return Shortcut("EmptyGesture", "{\"Version\":1,\"Bindings\":{\"editor.completion.show\":[\"\"]}}", "Gesto vazio");
        yield return Shortcut("NullGesture", "{\"Version\":1,\"Bindings\":{\"editor.completion.show\":[null]}}", "Gesto vazio");
        yield return Shortcut("UnknownKeyName", "{\"Version\":1,\"Bindings\":{\"editor.inline.dismiss\":[\"Ctrl+Esc\"]}}", "Tecla 'Esc' desconhecida");
        yield return Shortcut("TypingKeyWithoutModifier", "{\"Version\":1,\"Bindings\":{\"editor.completion.show\":[\".\"]}}", "exigem Ctrl, Alt ou Meta");
        yield return Shortcut("Version2", "{\"Version\":2,\"Bindings\":{}}", "versão 2 não suportada");
        yield return Shortcut("Version0", "{\"Version\":0,\"Bindings\":{}}", "versão 0 não suportada");
        yield return Shortcut("NullBindings", "{\"Version\":1,\"Bindings\":null}", "lista de atalhos ausente");
        yield return Shortcut("NullGestureList", "{\"Version\":1,\"Bindings\":{\"editor.completion.show\":null}}", "lista de gestos nula para 'editor.completion.show'");
        yield return Shortcut("ConflictWithDefault", "{\"Version\":1,\"Bindings\":{\"editor.completion.show\":[\"Tab\"]}}", "gesto 'Tab' atribuído a 'editor.completion.show' e 'editor.inline.accept'");
        yield return Shortcut("RepeatedGesture", "{\"Version\":1,\"Bindings\":{\"editor.completion.show\":[\"Ctrl+.\",\"ctrl+.\"]}}", "gesto 'Ctrl+.' repetido em 'editor.completion.show'");
        yield return Shortcut("ArrayInsteadOfObject", "[]", null);
        yield return Shortcut("TextInsteadOfList", "{\"Version\":1,\"Bindings\":{\"editor.completion.show\":\"Ctrl+.\"}}", null);
        yield return new TestCaseData("Autocomplete/CompletionEnterAccepts", "\"sim\"", null).SetName("InvalidPreferenceKeepsSession_EnterAcceptsText");
        yield return new TestCaseData("Autocomplete/CompletionAutoOpenOnTrigger", "1", null).SetName("InvalidPreferenceKeepsSession_AutoOpenNumber");
    }

    [TestCaseSource(nameof(InvalidPreferences))]
    public async Task InvalidPreferenceMakesTheSessionUnreadableVisiblyAndItIsNeverOverwritten(string property, string json, string? reason)
    {
        var path = NewDatabasePath();
        var original = SessionJson(preferences => Set(preferences, property, json));
        WriteRawSession(path, original);
        using var context = new WorkspaceTestContext();
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            var failure = Assert.CatchAsync(() => repository.LoadSessionAsync());
            if (reason is not null) Assert.That(failure, Is.TypeOf<InvalidDataException>().And.Message.Contains(reason));
            using var workspace = new WorkspaceViewModel(context.Workspace, repository);
            await workspace.InitializeAsync();
            Assert.Multiple(() =>
            {
                Assert.That(workspace.SessionStatus, Does.Contain("Não foi possível recuperar").And.Contain(reason ?? ""));
                Assert.That(Texts(workspace.KeyBindings), Is.EqualTo(ExpectedDefaults), "Os padrões valem só em memória.");
            });
            workspace.ActiveTab!.Text = "db.clientes.find({})";
            Assert.CatchAsync<InvalidOperationException>(() => workspace.SetUuidRepresentationAsync(UuidRepresentation.JavaLegacy));
            Assert.CatchAsync(() => repository.SaveSessionAsync(new WorkspaceSession()));
            Assert.CatchAsync(() => repository.SaveSessionAsync(new WorkspaceSession { Preferences = new() { EditorKeyBindings = new() } }));
        }
        Assert.That(ReadRawSession(path), Is.EqualTo(original));
    }

    [Test]
    public async Task AutosaveNeverReplacesASessionWithUnsupportedShortcutVersion()
    {
        var path = NewDatabasePath();
        var original = SessionJson(preferences => preferences["EditorKeyBindings"] = JsonNode.Parse("{\"Version\":2,\"Bindings\":{}}"));
        WriteRawSession(path, original);
        using var context = new WorkspaceTestContext();
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            using var workspace = new WorkspaceViewModel(context.Workspace, repository);
            await workspace.InitializeAsync();
            workspace.ActiveTab!.Text = "rascunho novo";
            await Task.Delay(1100); // Longer than the 750 ms autosave debounce.
            Assert.That(workspace.SessionStatus, Does.Contain("Não foi possível recuperar").And.Contain("versão 2 não suportada"));
        }
        Assert.That(ReadRawSession(path), Is.EqualTo(original));
    }

    [Test]
    public async Task WorkspaceRejectsInvalidShortcutsEvenWhenTheRepositoryReturnsThemUnvalidated()
    {
        using var context = new WorkspaceTestContext();
        var repository = new FixedSessionRepository(new WorkspaceSession
        {
            Preferences = new() { EditorKeyBindings = new() { Bindings = new() { ["editor.completion.show"] = ["Ctrl+Nope"] } } }
        });
        using var workspace = new WorkspaceViewModel(context.Workspace, repository);
        await workspace.InitializeAsync();
        workspace.ActiveTab!.Text = "rascunho novo";
        await Task.Delay(1100);
        Assert.Multiple(() =>
        {
            Assert.That(workspace.SessionStatus, Does.Contain("Não foi possível recuperar").And.Contain("Tecla 'Nope' desconhecida"));
            Assert.CatchAsync<InvalidOperationException>(() => workspace.SaveSessionAsync());
            Assert.That(repository.SaveAttempts, Is.Zero);
        });
    }

    [Test]
    public async Task SavingInvalidShortcutsIsRejectedBeforeWritingAndKeepsThePreviousSession()
    {
        using var context = new WorkspaceTestContext();
        await context.Repository.SaveSessionAsync(new WorkspaceSession { Preferences = new() { Theme = "Escuro" } });
        var failure = Assert.CatchAsync<InvalidDataException>(() => context.Repository.SaveSessionAsync(
            new WorkspaceSession { Preferences = new() { Theme = "Claro", EditorKeyBindings = new() { Bindings = new() { ["editor.inline.dismiss"] = null! } } } }));
        Assert.That(failure!.Message, Does.Contain("lista de gestos nula para 'editor.inline.dismiss'"));
        var kept = (await context.Repository.LoadSessionAsync()).Preferences;
        Assert.That((kept.Theme, kept.EditorKeyBindings), Is.EqualTo(("Escuro", (EditorKeyBindings?)null)));
    }

    private sealed class FixedSessionRepository(WorkspaceSession session) : IWorkspaceSessionRepository
    {
        public int SaveAttempts { get; private set; }
        public Task<WorkspaceSession> LoadSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(session);
        public Task SaveSessionAsync(WorkspaceSession value, CancellationToken cancellationToken = default) { SaveAttempts++; return Task.CompletedTask; }
    }

    private static Dictionary<string, string[]> Texts(IReadOnlyDictionary<string, IReadOnlyList<EditorKeyGesture>> bindings) =>
        bindings.ToDictionary(entry => entry.Key, entry => entry.Value.Select(gesture => gesture.ToString()).ToArray());

    private static void Set(JsonObject preferences, string property, string json)
    {
        var segments = property.Split('/');
        var target = preferences;
        foreach (var segment in segments[..^1]) target = target[segment]!.AsObject();
        target[segments[^1]] = JsonNode.Parse(json);
    }

    /// <summary>Version 1 session as written before the shortcut and list-flag fields existed.</summary>
    private static string SessionJson(Action<JsonObject>? change = null)
    {
        var session = JsonSerializer.SerializeToNode(new WorkspaceSession { Preferences = new() { Theme = "Escuro" } })!.AsObject();
        var preferences = session["Preferences"]!.AsObject();
        preferences.Remove(nameof(WorkspacePreferences.EditorKeyBindings));
        preferences["Autocomplete"]!.AsObject().Remove(nameof(AutocompleteSettings.CompletionAutoOpenOnTrigger));
        preferences["Autocomplete"]!.AsObject().Remove(nameof(AutocompleteSettings.CompletionEnterAccepts));
        change?.Invoke(preferences);
        return session.ToJsonString();
    }

    private static string NewDatabasePath() =>
        Path.Combine(TestContext.CurrentContext.WorkDirectory, "shortcut-session-" + Guid.NewGuid().ToString("N"), "workspace.db");

    private static void WriteRawSession(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var database = new LiteDB.LiteDatabase($"Filename={path};Connection=direct");
        database.GetCollection("workspaceSession").Upsert(new LiteDB.BsonDocument { ["_id"] = "current", ["json"] = json });
    }

    private static string ReadRawSession(string path)
    {
        using var database = new LiteDB.LiteDatabase($"Filename={path};Connection=direct");
        return database.GetCollection("workspaceSession").FindById("current")["json"].AsString;
    }
}
