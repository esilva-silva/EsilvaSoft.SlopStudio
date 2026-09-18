namespace EsilvaSoft.SlopStudio.Core;

/// <summary>
/// Matches persisted editor gestures without taking a dependency on a UI toolkit.
/// A produced symbol is authoritative for punctuation: a physical US key must not
/// accidentally invoke a command when the active keyboard layout produced another symbol.
/// </summary>
public sealed class EditorCommandDispatcher(IReadOnlyDictionary<string, IReadOnlyList<EditorKeyGesture>> bindings)
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<EditorKeyGesture>> _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));

    public string? Match(EditorKeyEvent keyEvent)
    {
        foreach (var command in EditorCommandIds.All)
        {
            if (!_bindings.TryGetValue(command, out var gestures)) continue;
            foreach (var gesture in gestures)
                if (Matches(gesture, keyEvent)) return command;
        }
        return null;
    }

    private static bool Matches(EditorKeyGesture gesture, EditorKeyEvent keyEvent)
    {
        if (gesture.Modifiers != keyEvent.Modifiers) return false;
        if (keyEvent.Symbol is { } symbol)
            return gesture.Symbol == symbol;
        return gesture.Key == keyEvent.PhysicalKey || gesture.Symbol == keyEvent.PhysicalSymbol;
    }
}
