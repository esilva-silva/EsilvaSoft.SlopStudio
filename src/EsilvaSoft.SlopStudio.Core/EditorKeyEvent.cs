namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Normalized key data supplied by the desktop shell to <see cref="EditorCommandDispatcher"/>.</summary>
public readonly record struct EditorKeyEvent(EditorKeyModifiers Modifiers, char? Symbol, EditorKey? PhysicalKey, char? PhysicalSymbol = null);
