using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>Shared expectations for the editor key-bindings domain tests and their session-persistence counterpart.</summary>
internal static class EditorKeyBindingsTestFixture
{
    // Independent fixture written by hand from the shortcut table, not derived from EditorKeyBindings.Defaults.
    public static readonly Dictionary<string, string[]> ExpectedDefaults = new()
    {
        ["editor.completion.show"] = ["Ctrl+.", "Ctrl+Space"],
        ["editor.completion.ai"] = ["Ctrl+;"],
        ["editor.inline.accept"] = ["Tab"],
        ["editor.inline.dismiss"] = ["Escape"]
    };

    public static Dictionary<string, string[]> Texts(IReadOnlyDictionary<string, IReadOnlyList<EditorKeyGesture>> bindings) =>
        bindings.ToDictionary(entry => entry.Key, entry => entry.Value.Select(gesture => gesture.ToString()).ToArray());
}
