using System.Text;

namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Stable editor command identifiers persisted in <see cref="EditorKeyBindings"/>.</summary>
public static class EditorCommandIds
{
    public const string CompletionShow = "editor.completion.show";
    /// <summary>Reserved for explicit AI completion; no handler is bound yet.</summary>
    public const string CompletionAi = "editor.completion.ai";
    public const string InlineAccept = "editor.inline.accept";
    public const string InlineDismiss = "editor.inline.dismiss";
    public static IReadOnlyList<string> All { get; } = [CompletionShow, CompletionAi, InlineAccept, InlineDismiss];
}

[Flags]
public enum EditorKeyModifiers { None = 0, Control = 1, Shift = 2, Alt = 4, Meta = 8 }

/// <summary>Named keys matched by key identity. Digits are written "0".."9" and letters "A".."Z".</summary>
public enum EditorKey
{
    Space, Tab, Enter, Escape, Backspace, Delete, Insert, Home, End, PageUp, PageDown, Up, Down, Left, Right,
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24
}

/// <summary>
/// Layout-independent gesture: modifiers plus either a named <see cref="Key"/> or a single ASCII punctuation
/// <see cref="Symbol"/>. The desktop matches symbols by the produced character first and the physical key second.
/// Grammar: <c>[Modifier "+"]* (KeyName | Punctuation)</c>, modifiers <c>Ctrl Shift Alt Meta</c> (any order, no repeats),
/// names case-insensitive, no whitespace; <c>"Ctrl++"</c> binds the plus sign. Character-producing keys (letters, digits,
/// Space, punctuation) require Ctrl, Alt or Meta so a binding never swallows typed text.
/// </summary>
public readonly record struct EditorKeyGesture
{
    private static readonly (string Name, EditorKeyModifiers Value)[] ModifierNames =
        [("Ctrl", EditorKeyModifiers.Control), ("Shift", EditorKeyModifiers.Shift), ("Alt", EditorKeyModifiers.Alt), ("Meta", EditorKeyModifiers.Meta)];
    private static readonly Dictionary<string, EditorKey> KeysByName =
        Enum.GetValues<EditorKey>().ToDictionary(NameOf, key => key, StringComparer.OrdinalIgnoreCase);

    public EditorKeyModifiers Modifiers { get; }
    public EditorKey? Key { get; }
    public char? Symbol { get; }

    private EditorKeyGesture(EditorKeyModifiers modifiers, EditorKey? key, char? symbol) => (Modifiers, Key, Symbol) = (modifiers, key, symbol);

    public static EditorKeyGesture ForKey(EditorKey key, EditorKeyModifiers modifiers = EditorKeyModifiers.None) =>
        Create(modifiers, key, null, out var error) ?? throw new ArgumentException(error);

    public static EditorKeyGesture ForSymbol(char symbol, EditorKeyModifiers modifiers = EditorKeyModifiers.None) =>
        Create(modifiers, null, symbol, out var error) ?? throw new ArgumentException(error);

    public static bool IsPunctuation(char value) => value is > ' ' and < '\x7f' && !char.IsAsciiLetterOrDigit(value);

    public static EditorKeyGesture Parse(string? text) => TryParse(text, out var gesture, out var error) ? gesture : throw new FormatException(error);

    public static bool TryParse(string? text, out EditorKeyGesture gesture) => TryParse(text, out gesture, out _);

    public static bool TryParse(string? text, out EditorKeyGesture gesture, out string error)
    {
        gesture = default;
        if (string.IsNullOrEmpty(text)) { error = "Gesto vazio."; return false; }
        if (text.Any(char.IsWhiteSpace)) { error = $"Gesto '{text}' contém espaço; use 'Space' para a barra de espaço."; return false; }
        string keyToken, modifierPart;
        if (text[^1] == '+')
        {
            keyToken = "+";
            modifierPart = text[..^1];
            if (modifierPart.Length > 0 && modifierPart[^1] != '+') { error = $"Gesto '{text}' sem tecla."; return false; }
            if (modifierPart.Length > 0) modifierPart = modifierPart[..^1];
        }
        else
        {
            var separator = text.LastIndexOf('+');
            keyToken = text[(separator + 1)..];
            modifierPart = separator < 0 ? "" : text[..separator];
            if (separator == 0) { error = $"Gesto '{text}' com modificador vazio."; return false; }
        }
        var modifiers = EditorKeyModifiers.None;
        if (modifierPart.Length > 0)
            foreach (var part in modifierPart.Split('+'))
            {
                var match = Array.FindIndex(ModifierNames, entry => string.Equals(entry.Name, part, StringComparison.OrdinalIgnoreCase));
                if (match < 0) { error = $"Modificador '{part}' desconhecido em '{text}'; use Ctrl, Shift, Alt ou Meta."; return false; }
                if ((modifiers & ModifierNames[match].Value) != 0) { error = $"Modificador '{part}' repetido em '{text}'."; return false; }
                modifiers |= ModifierNames[match].Value;
            }
        EditorKeyGesture? created;
        if (keyToken.Length == 1 && IsPunctuation(keyToken[0])) created = Create(modifiers, null, keyToken[0], out error);
        else if (KeysByName.TryGetValue(keyToken, out var key)) created = Create(modifiers, key, null, out error);
        else { error = $"Tecla '{keyToken}' desconhecida em '{text}'."; return false; }
        if (created is null) { error = $"Gesto '{text}': {error}"; return false; }
        gesture = created.Value;
        return true;
    }

    /// <summary>Canonical text: modifiers in Ctrl, Shift, Alt, Meta order, then the key name or symbol.</summary>
    public override string ToString()
    {
        if (Key is null && Symbol is null) return "";
        var text = new StringBuilder();
        foreach (var (name, value) in ModifierNames)
            if ((Modifiers & value) != 0) text.Append(name).Append('+');
        return Key is { } key ? text.Append(NameOf(key)).ToString() : text.Append(Symbol!.Value).ToString();
    }

    private static EditorKeyGesture? Create(EditorKeyModifiers modifiers, EditorKey? key, char? symbol, out string error)
    {
        error = "";
        if ((modifiers & ~(EditorKeyModifiers.Control | EditorKeyModifiers.Shift | EditorKeyModifiers.Alt | EditorKeyModifiers.Meta)) != 0) error = "modificador inválido.";
        else if (key is { } named && !Enum.IsDefined(named)) error = "tecla inválida.";
        else if (symbol is { } character && !IsPunctuation(character)) error = "símbolo deve ser um único caractere de pontuação ASCII.";
        else if ((key is null) == (symbol is null)) error = "informe exatamente uma tecla ou um símbolo.";
        else if (ProducesText(key, symbol) && (modifiers & (EditorKeyModifiers.Control | EditorKeyModifiers.Alt | EditorKeyModifiers.Meta)) == 0)
            error = "teclas que produzem texto exigem Ctrl, Alt ou Meta.";
        return error.Length == 0 ? new EditorKeyGesture(modifiers, key, symbol) : null;
    }

    private static bool ProducesText(EditorKey? key, char? symbol) =>
        symbol is not null || key is EditorKey.Space or >= EditorKey.A and <= EditorKey.Z or >= EditorKey.D0 and <= EditorKey.D9;

    private static string NameOf(EditorKey key) => key is >= EditorKey.D0 and <= EditorKey.D9 ? ((char)('0' + (key - EditorKey.D0))).ToString() : key.ToString();
}

/// <summary>
/// Additive to session version 1. <see cref="Bindings"/> overrides <see cref="Defaults"/> per command: a command without an
/// entry keeps its defaults and an empty list leaves it unbound. Unknown commands, malformed gestures, null lists, a gesture
/// bound twice and versions other than 1 are invalid and make the whole session unreadable.
/// </summary>
public sealed record EditorKeyBindings
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public Dictionary<string, string[]> Bindings { get; init; } = [];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Defaults { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        [EditorCommandIds.CompletionShow] = ["Ctrl+.", "Ctrl+Space"],
        [EditorCommandIds.CompletionAi] = ["Ctrl+;"],
        [EditorCommandIds.InlineAccept] = ["Tab"],
        [EditorCommandIds.InlineDismiss] = ["Escape"]
    };

    /// <summary>Throws <see cref="InvalidDataException"/> with a visible reason; returns this instance when valid.</summary>
    public EditorKeyBindings Validate()
    {
        _ = Build(this);
        return this;
    }

    /// <summary>Effective gestures for every known command; null (absent from the session) yields the defaults.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<EditorKeyGesture>> Resolve(EditorKeyBindings? bindings) => Build(bindings);

    private static Dictionary<string, IReadOnlyList<EditorKeyGesture>> Build(EditorKeyBindings? bindings)
    {
        if (bindings is not null)
        {
            if (bindings.Version != CurrentVersion) throw Invalid($"versão {bindings.Version} não suportada.");
            if (bindings.Bindings is null) throw Invalid("lista de atalhos ausente.");
            foreach (var command in bindings.Bindings.Keys)
                if (!Defaults.ContainsKey(command)) throw Invalid($"comando '{command}' desconhecido.");
        }
        var effective = new Dictionary<string, IReadOnlyList<EditorKeyGesture>>(StringComparer.Ordinal);
        var owners = new Dictionary<EditorKeyGesture, string>();
        foreach (var command in EditorCommandIds.All)
        {
            string[]? custom = null;
            var overridden = bindings is not null && bindings.Bindings.TryGetValue(command, out custom);
            IReadOnlyList<string>? texts = overridden ? custom : Defaults[command];
            if (texts is null) throw Invalid($"lista de gestos nula para '{command}'.");
            var gestures = new EditorKeyGesture[texts.Count];
            for (var index = 0; index < texts.Count; index++)
            {
                if (!EditorKeyGesture.TryParse(texts[index], out var gesture, out var error)) throw Invalid($"{error} (comando '{command}').");
                if (!owners.TryAdd(gesture, command))
                    throw Invalid(owners[gesture] == command ? $"gesto '{gesture}' repetido em '{command}'." : $"gesto '{gesture}' atribuído a '{owners[gesture]}' e '{command}'.");
                gestures[index] = gesture;
            }
            effective[command] = gestures;
        }
        return effective;
    }

    private static InvalidDataException Invalid(string reason) => new("Preferência de atalhos do editor inválida: " + reason);
}
