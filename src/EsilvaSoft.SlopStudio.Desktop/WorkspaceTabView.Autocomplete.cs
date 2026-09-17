using EsilvaSoft.SlopStudio.Desktop.SyntaxHighlighting;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.VisualTree;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;
using EsilvaSoft.SlopStudio.Autocomplete.Core.SyntaxHighlighting;

namespace EsilvaSoft.SlopStudio.Desktop;

/// <summary>
/// Inline (ghost text) completion lifecycle and the keyboard dispatch shared with the traditional completion list
/// (see <c>WorkspaceTabView.Autocomplete.Traditional.cs</c>) and snippet placeholders
/// (see <c>WorkspaceTabView.Autocomplete.Snippets.cs</c>).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Avalonia controls release editor sessions on DetachedFromVisualTree and recreate them on attachment.")]
public partial class WorkspaceTabView
{
    private CompletionSession _completionSession = new();
    private WorkspaceTabViewModel? _completionTab;
    private AutocompleteResult? _completion;
    private string? _completionOriginal;
    private int _completionCaret;
    private bool _attached;
    private bool _acceptingCompletion;
    private SnippetSession? _snippetSession;
    private readonly CompletionWindowPresenter _traditionalPresenter = new();
    private CancellationTokenSource? _traditionalDocumentationCancellation;
    private long _traditionalDocumentationGeneration;
    private string? _traditionalCompletionDocument;
    private bool _traditionalCompletionIncomplete;

    private void InitializeAutocomplete()
    {
        CodeEditor.AddHandler(InputElement.KeyDownEvent, EditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        CodeEditor.PropertyChanged += EditorCompletionChanged;
        CodeEditor.LayoutUpdated += (_, _) => PositionGhostText();
        CodeEditor.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => PositionGhostText());
        CodeEditor.LostFocus += (_, _) => InvalidateCompletion();
        DataContextChanged += (_, _) => BindCompletionTab();
        AttachedToVisualTree += (_, _) => { _completionSession.Dispose(); _completionSession = new(); _attached = true; BindCompletionTab(); };
        DetachedFromVisualTree += (_, _) => { _attached = false; InvalidateCompletion(); _completionSession.Dispose(); UnbindCompletionTab(); };
    }

    private void BindCompletionTab()
    {
        InvalidateCompletion(); UnbindCompletionTab();
        if (!_attached || DataContext is not WorkspaceTabViewModel tab) return;
        _completionTab = tab;
        tab.PropertyChanged += CompletionContextChanged;
        tab.TraditionalCompletionRefreshRequested += TraditionalCompletionRefreshRequested;
        tab.Autocomplete.SettingsChanged += CompletionSettingsChanged;
    }

    private void UnbindCompletionTab()
    {
        if (_completionTab is not { } tab) return;
        tab.PropertyChanged -= CompletionContextChanged;
        tab.TraditionalCompletionRefreshRequested -= TraditionalCompletionRefreshRequested;
        tab.Autocomplete.SettingsChanged -= CompletionSettingsChanged;
        _completionTab = null;
    }

    private void CompletionSettingsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(InvalidateCompletion);
    private void TraditionalCompletionRefreshRequested(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_traditionalPresenter.IsOpen && DataContext == sender && CodeEditor.SelectionStart == CodeEditor.SelectionEnd)
            ShowTraditionalCompletionList(this, new Avalonia.Interactivity.RoutedEventArgs());
    });
    private void CompletionContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_acceptingCompletion) return;
        if (e.PropertyName is nameof(WorkspaceTabViewModel.Text) or nameof(WorkspaceTabViewModel.Profile)
            or nameof(WorkspaceTabViewModel.Database) or nameof(WorkspaceTabViewModel.Collection) or nameof(WorkspaceTabViewModel.Mode)
            or nameof(WorkspaceTabViewModel.InputJson) or nameof(WorkspaceTabViewModel.Results))
            InvalidateCompletion();
    }

    private void InvalidateCompletion()
    {
        _completionSession.Invalidate();
        _completionCancellation?.Cancel();
        _completion = null; _completionOriginal = null;
        CompletionPanel.IsVisible = false;
        CloseTraditionalCompletion();
    }

    private async void EditorCompletionChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_acceptingCompletion) return;
        if (e.Property != MongoTextEditor.TextProperty && e.Property != MongoTextEditor.CaretIndexProperty
            && e.Property != MongoTextEditor.SelectionStartProperty && e.Property != MongoTextEditor.SelectionEndProperty) return;
        _snippetSession = null;
        if (_traditionalPresenter.IsOpen && e.Property == MongoTextEditor.TextProperty && CodeEditor.SelectionStart == CodeEditor.SelectionEnd)
        {
            _traditionalPresenter.SetFilter(CurrentCompletionPrefix());
            RefreshTraditionalCompletionList();
            return;
        }
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        InvalidateCompletion();
        if (!_attached || !CodeEditor.IsKeyboardFocusWithin || CodeEditor.SelectionStart != CodeEditor.SelectionEnd
            || DataContext is not WorkspaceTabViewModel tab || !tab.Autocomplete.Settings.Enabled) return;
        var original = CodeEditor.Text ?? "";
        var caret = Math.Clamp(CodeEditor.CaretIndex, 0, original.Length);
        var profile = tab.Profile; var database = tab.Database; var mode = tab.Mode;
        var request = tab.CaptureAutocompleteRequest(original, caret);
        try
        {
            var pending = _completionSession.RequestAsync(tab.Autocomplete, request);
            // Synchronous work of this editor event on the UI thread, including the immediate dictionary lookup.
            AutocompleteMetrics.UiDispatcherTime.Record(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("handler", "inline"));
            var result = await pending;
            if (result is null || !_attached || !CodeEditor.IsKeyboardFocusWithin || DataContext != tab || CodeEditor.Text != original
                || CodeEditor.CaretIndex != caret || CodeEditor.SelectionStart != CodeEditor.SelectionEnd
                || tab.Profile != profile || tab.Database != database || tab.Mode != mode) return;
            _completion = result; _completionOriginal = original; _completionCaret = caret;
            CompletionPanel.IsVisible = true;
            PositionGhostText();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { tab.Messages = "Sugestão não disponível; use Ctrl+Espaço para sugestões de Console/MQL."; }
    }

    private bool AcceptCompletion()
    {
        if (_completion is not { } completion || _completionOriginal is not { } original || DataContext is not WorkspaceTabViewModel tab
            || CodeEditor.Text != original || CodeEditor.CaretIndex != _completionCaret || !tab.Autocomplete.Settings.Enabled) return false;
        var caret = _completionCaret;
        var count = tab.Autocomplete.Settings.IncrementalTab ? IncrementalCompletion.NextLength(completion.Text) : completion.Text.Length;
        InvalidateCompletion();
        _acceptingCompletion = true;
        try
        {
            CodeEditor.SelectionStart = CodeEditor.SelectionEnd = caret;
            CodeEditor.SelectedText = completion.Text[..count];
            CodeEditor.CaretIndex = caret + count;
            CodeEditor.SelectionStart = CodeEditor.SelectionEnd = CodeEditor.CaretIndex;
        }
        finally { _acceptingCompletion = false; }
        if (count < completion.Text.Length)
        {
            _completion = completion with { Text = completion.Text[count..] };
            _completionOriginal = CodeEditor.Text; _completionCaret = caret + count;
            CompletionPanel.IsVisible = true;
            PositionGhostText();
        }
        return true;
    }

    private void PositionGhostText()
    {
        if (!CompletionPanel.IsVisible || _completion is null || _completionOriginal is not { } original) return;
        var textView = CodeEditor.TextArea.TextView;
        CompletionPanel.Background = CodeEditor.Background;
        if (textView.TranslatePoint(default, GhostLayer) is { } viewportOrigin)
            GhostLayer.Clip = new RectangleGeometry(new Rect(viewportOrigin, textView.Bounds.Size));
        var caret = _completionCaret;
        var lineRange = CodeEditor.VisibleLineRange(caret);
        var lineStart = lineRange.Start;
        var suffixEnd = CodeEditor.Document.GetLineByOffset(caret).Length > SyntaxHighlightingOptions.LongLineThreshold
            ? lineRange.End : Math.Min(original.Length, caret + 32768);
        if (textView.TranslatePoint(CodeEditor.PositionInTextView(lineStart), GhostLayer) is not { } point) return;
        Canvas.SetLeft(CompletionPanel, point.X); Canvas.SetTop(CompletionPanel, point.Y);
        CompletionPanel.Width = Math.Max(0, GhostLayer.Bounds.Width - point.X - 16);
        CompletionPanel.Height = Math.Max(0, GhostLayer.Bounds.Height - point.Y - 16);
        if (textView.TranslatePoint(CodeEditor.PositionInTextView(caret), GhostLayer) is { } caretPoint)
        {
            Canvas.SetLeft(GhostCaret, caretPoint.X); Canvas.SetTop(GhostCaret, caretPoint.Y);
        }
        CompletionText.Show(original[lineStart..caret], _completion.Text, original[caret..suffixEnd],
            SyntaxHighlighting.SyntaxStyles.Brush(CodeEditor, SyntaxTokenType.Default),
            SyntaxHighlighting.SyntaxStyles.Brush(CompletionText, SyntaxTokenType.GhostText),
            CodeEditor.Snapshot is { } snapshot && snapshot.Text == original ? snapshot : null, lineStart);
    }

    public bool DismissCompletion()
    {
        if (!CompletionPanel.IsVisible && !_traditionalPresenter.IsOpen) return false;
        InvalidateCompletion(); return true;
    }

    private void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        var tab = DataContext as WorkspaceTabViewModel;
        var command = tab is null ? null : new EditorCommandDispatcher(tab.KeyBindings).Match(ToEditorKeyEvent(e));
        if (_traditionalPresenter.IsOpen)
        {
            if (e.Key == Key.Down) { _traditionalPresenter.Move(1); RefreshTraditionalCompletionList(); e.Handled = true; return; }
            if (e.Key == Key.Up) { _traditionalPresenter.Move(-1); RefreshTraditionalCompletionList(); e.Handled = true; return; }
            if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None && AcceptTraditionalCompletion()) { e.Handled = true; return; }
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                if (tab?.Autocomplete.Settings.CompletionEnterAccepts != false && AcceptTraditionalCompletion()) e.Handled = true;
                else CloseTraditionalCompletion();
                return;
            }
            if (e.Key == Key.Escape) { CloseTraditionalCompletion(); e.Handled = true; return; }
        }
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None && MoveSnippetPlaceholder(reverse: false)) { e.Handled = true; return; }
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Shift && MoveSnippetPlaceholder(reverse: true)) { e.Handled = true; return; }
        if (command == EditorCommandIds.InlineAccept && AcceptCompletion()) { e.Handled = true; return; }
        if (command == EditorCommandIds.InlineDismiss && CompletionPanel.IsVisible) { InvalidateCompletion(); e.Handled = true; return; }
        if (command == EditorCommandIds.InlineDismiss && _snippetSession is not null) { _snippetSession = null; e.Handled = true; return; }
        if (command == EditorCommandIds.CompletionShow) { e.Handled = true; ShowTraditionalCompletionList(sender, e); }
    }

    private static EditorKeyEvent ToEditorKeyEvent(KeyEventArgs e)
    {
        var modifiers = (e.KeyModifiers.HasFlag(KeyModifiers.Control) ? EditorKeyModifiers.Control : EditorKeyModifiers.None)
            | (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? EditorKeyModifiers.Shift : EditorKeyModifiers.None)
            | (e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? EditorKeyModifiers.Alt : EditorKeyModifiers.None)
            | (e.KeyModifiers.HasFlag(KeyModifiers.Meta) ? EditorKeyModifiers.Meta : EditorKeyModifiers.None);
        char? symbol = e.KeySymbol is { Length: 1 } text ? text[0] : null;
        var qwertyKey = e.PhysicalKey.ToQwertyKey();
        EditorKey? physical = Enum.TryParse<EditorKey>(qwertyKey.ToString(), ignoreCase: true, out var parsed) ? parsed : null;
        var qwertySymbol = e.PhysicalKey.ToQwertyKeySymbol(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        char? physicalSymbol = qwertySymbol is { Length: 1 } physicalText ? physicalText[0] : null;
        return new EditorKeyEvent(modifiers, symbol, physical, physicalSymbol);
    }
}
