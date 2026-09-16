using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using static EsilvaSoft.SlopStudio.UnitTests.EditorKeyBindingsTestFixture;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class EditorKeyBindingsTests
{
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
}
