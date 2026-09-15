using EsilvaSoft.SlopStudio.Desktop.SyntaxHighlighting;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using Avalonia.Media;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.Desktop;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Avalonia controls release editor sessions on DetachedFromVisualTree and recreate them on attachment.")]
public partial class WorkspaceTabView
{
    private CompletionSession _completionSession = new();
    private WorkspaceTabViewModel? _completionTab;
    private AutocompleteResult? _completion;
    private string? _completionOriginal;
    private int _completionCaret;
    private bool _attached;
    private MenuFlyout? _completionMenu;
    private bool _acceptingCompletion;

    private void InitializeAutocomplete()
    {
        CodeEditor.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, EditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        CodeEditor.PropertyChanged += EditorCompletionChanged;
        CodeEditor.LayoutUpdated += (_, _) => PositionGhostText();
        CodeEditor.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => PositionGhostText());
        CodeEditor.LostFocus += (_, _) => { if (_completionMenu is null) InvalidateCompletion(); };
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
        tab.Autocomplete.SettingsChanged += CompletionSettingsChanged;
    }

    private void UnbindCompletionTab()
    {
        if (_completionTab is not { } tab) return;
        tab.PropertyChanged -= CompletionContextChanged;
        tab.Autocomplete.SettingsChanged -= CompletionSettingsChanged;
        _completionTab = null;
    }

    private void CompletionSettingsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(InvalidateCompletion);
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
        _completionMenu?.Hide(); _completionMenu = null;
    }

    private async void EditorCompletionChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_acceptingCompletion) return;
        if (e.Property != MongoTextEditor.TextProperty && e.Property != MongoTextEditor.CaretIndexProperty
            && e.Property != MongoTextEditor.SelectionStartProperty && e.Property != MongoTextEditor.SelectionEndProperty) return;
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
            Application.Language.AutocompleteMetrics.UiDispatcherTime.Record(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
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
        var suffixEnd = CodeEditor.Document.GetLineByOffset(caret).Length > Application.SyntaxHighlighting.SyntaxHighlightingOptions.LongLineThreshold
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
            SyntaxHighlighting.SyntaxStyles.Brush(CodeEditor, Application.SyntaxHighlighting.SyntaxTokenType.Default),
            SyntaxHighlighting.SyntaxStyles.Brush(CompletionText, Application.SyntaxHighlighting.SyntaxTokenType.GhostText),
            CodeEditor.Snapshot is { } snapshot && snapshot.Text == original ? snapshot : null, lineStart);
    }

    public bool DismissCompletion()
    {
        if (!CompletionPanel.IsVisible && _completionMenu is null) return false;
        InvalidateCompletion(); return true;
    }
}
