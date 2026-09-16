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
using EsilvaSoft.SlopStudio.Application.Language.Completion;
using EsilvaSoft.SlopStudio.Application.Language.Text;

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
    private bool _acceptingCompletion;
    private SnippetSession? _snippetSession;
    private readonly CompletionWindowPresenter _traditionalPresenter = new();
    private CancellationTokenSource? _traditionalDocumentationCancellation;
    private long _traditionalDocumentationGeneration;
    private string? _traditionalCompletionDocument;
    private bool _traditionalCompletionIncomplete;

    private void InitializeAutocomplete()
    {
        CodeEditor.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, EditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
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

    private void ShowTraditionalCompletion(IEnumerable<CompletionItem> items, bool isIncomplete = false)
    {
        _traditionalPresenter.Show(items);
        if (_traditionalPresenter.Items.Count == 0) { _traditionalPresenter.Close(); return; }
        _traditionalCompletionDocument = CodeEditor.Text ?? "";
        _traditionalCompletionIncomplete = isIncomplete;
        TraditionalCompletionPanel.IsVisible = true;
        var availableWidth = Math.Max(0, TraditionalCompletionLayer.Bounds.Width);
        var panelWidth = Math.Min(400, availableWidth);
        TraditionalCompletionPanel.Width = panelWidth > 0 ? panelWidth : 400;
        RefreshTraditionalCompletionList();
        var caret = Math.Clamp(CodeEditor.CaretIndex, 0, CodeEditor.Document.TextLength);
        if (CodeEditor.TextArea.TextView.TranslatePoint(CodeEditor.PositionInTextView(caret), TraditionalCompletionLayer) is { } point)
        {
            var effectiveWidth = TraditionalCompletionPanel.Width > 0 ? TraditionalCompletionPanel.Width : 400;
            var maxLeft = Math.Max(0, TraditionalCompletionLayer.Bounds.Width - effectiveWidth);
            Canvas.SetLeft(TraditionalCompletionPanel, Math.Clamp(point.X, 0, maxLeft));
            const double panelHeight = 180;
            var below = point.Y + CodeEditor.LineHeight;
            var top = below + panelHeight <= TraditionalCompletionLayer.Bounds.Height
                ? below
                : Math.Max(0, point.Y - panelHeight);
            Canvas.SetTop(TraditionalCompletionPanel, top);
        }
    }

    private void RefreshTraditionalCompletionList()
    {
        TraditionalCompletionList.ItemsSource = _traditionalPresenter.Items.ToArray();
        TraditionalCompletionList.SelectedItem = _traditionalPresenter.Selected;
        TraditionalCompletionStatus.Text = _traditionalPresenter.Items.Count == 0
            ? "Nenhuma sugestão corresponde ao texto atual"
            : $"{_traditionalPresenter.Items.Count} itens{(_traditionalCompletionIncomplete ? " · dados ainda carregando" : "")} · ↑↓ mover · Enter/Tab aceita · Esc fecha";
        ResolveTraditionalDocumentation(_traditionalPresenter.Selected);
    }

    private void CloseTraditionalCompletion()
    {
        _traditionalPresenter.Close();
        _traditionalDocumentationCancellation?.Cancel();
        _traditionalDocumentationCancellation = null;
        _traditionalDocumentationGeneration++;
        _traditionalCompletionDocument = null;
        _traditionalCompletionIncomplete = false;
        if (this.FindControl<Avalonia.Controls.TextBlock>("TraditionalCompletionDocumentation") is { } documentation) documentation.Text = "";
        if (this.FindControl<Avalonia.Controls.Border>("TraditionalCompletionPanel") is { } panel) panel.IsVisible = false;
    }

    private string CurrentCompletionPrefix()
    {
        var text = CodeEditor.Text ?? "";
        var caret = Math.Clamp(CodeEditor.CaretIndex, 0, text.Length);
        var start = caret;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '_' or '$')) start--;
        return text[start..caret];
    }

    private bool AcceptTraditionalCompletion()
    {
        var item = _traditionalPresenter.Selected;
        if (item is null) return false;
        var text = item.Edit.NewText;
        IReadOnlyList<SnippetPlaceholder>? placeholders = null;
        if (item.Edit.IsSnippet && SnippetTemplate.TryParse(text, out var template, out _))
        {
            var expansion = template!.Expand(); text = expansion.Text; placeholders = expansion.Placeholders;
        }
        var original = CodeEditor.Text ?? "";
        var caret = Math.Clamp(CodeEditor.CaretIndex, 0, original.Length);
        var exactEdit = string.Equals(original, _traditionalCompletionDocument, StringComparison.Ordinal);
        var replacement = exactEdit ? item.Edit.ReplaceRange : new TextSpan(caret - CurrentCompletionPrefix().Length, CurrentCompletionPrefix().Length);
        if (replacement.Start < 0 || replacement.End > original.Length) { CloseTraditionalCompletion(); return false; }
        var start = replacement.Start;
        _acceptingCompletion = true;
        try
        {
            using (CodeEditor.Document.RunUpdate()) CodeEditor.Document.Replace(start, replacement.Length, text);
            CodeEditor.CaretIndex = start + text.Length;
            CodeEditor.SelectionStart = CodeEditor.SelectionEnd = CodeEditor.CaretIndex;
        }
        finally { _acceptingCompletion = false; }
        CloseTraditionalCompletion();
        if (placeholders is not null) BeginSnippet(start, placeholders);
        return true;
    }

    private void TraditionalCompletionSelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (TraditionalCompletionList.SelectedItem is CompletionItem item)
        {
            _traditionalPresenter.Select(item.SymbolId);
            ResolveTraditionalDocumentation(item);
        }
    }

    private async void ResolveTraditionalDocumentation(CompletionItem? item)
    {
        _traditionalDocumentationCancellation?.Cancel();
        if (item is null || !TraditionalCompletionPanel.IsVisible) return;
        var generation = ++_traditionalDocumentationGeneration;
        var document = _traditionalCompletionDocument;
        var cancellation = new CancellationTokenSource();
        _traditionalDocumentationCancellation = cancellation;
        TraditionalCompletionDocumentation.Text = "Carregando detalhes…";
        try
        {
            var resolved = await Task.Run(() => CompletionDocumentationResolver.Resolve(item, cancellation.Token), cancellation.Token);
            if (generation != _traditionalDocumentationGeneration || cancellation.IsCancellationRequested || !TraditionalCompletionPanel.IsVisible
                || !string.Equals(document, CodeEditor.Text ?? "", StringComparison.Ordinal) || _traditionalPresenter.Selected?.SymbolId != item.SymbolId) return;
            TraditionalCompletionDocumentation.Text = resolved.Text;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_traditionalDocumentationCancellation, cancellation)) _traditionalDocumentationCancellation = null;
            cancellation.Dispose();
        }
    }

    private void AcceptTraditionalCompletion(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (AcceptTraditionalCompletion()) e.Handled = true;
    }

    private void BeginSnippet(int insertionStart, IReadOnlyList<SnippetPlaceholder> placeholders)
    {
        var ordered = placeholders.OrderBy(placeholder => placeholder.Index == 0 ? int.MaxValue : placeholder.Index)
            .Select(placeholder => new TextSpan(insertionStart + placeholder.Span.Start, placeholder.Span.Length)).ToArray();
        if (ordered.Length == 0) return;
        _snippetSession = new SnippetSession(CodeEditor.Text ?? "", ordered, 0);
        SelectSnippetPlaceholder(_snippetSession);
    }

    private bool MoveSnippetPlaceholder(bool reverse)
    {
        var session = _snippetSession;
        if (session is null || !string.Equals(CodeEditor.Text, session.DocumentText, StringComparison.Ordinal)) { _snippetSession = null; return false; }
        var next = session.Current + (reverse ? -1 : 1);
        if (next < 0 || next >= session.Placeholders.Length) { _snippetSession = null; return false; }
        session.Current = next;
        SelectSnippetPlaceholder(session);
        return true;
    }

    private void SelectSnippetPlaceholder(SnippetSession session)
    {
        var span = session.Placeholders[session.Current];
        _acceptingCompletion = true;
        try
        {
            CodeEditor.SelectionStart = span.Start;
            CodeEditor.SelectionEnd = span.End;
            CodeEditor.CaretIndex = span.Start;
        }
        finally { _acceptingCompletion = false; }
        CodeEditor.Focus();
    }

    private sealed class SnippetSession(string documentText, TextSpan[] placeholders, int current)
    {
        public string DocumentText { get; } = documentText;
        public TextSpan[] Placeholders { get; } = placeholders;
        public int Current { get; set; } = current;
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
        if (!CompletionPanel.IsVisible && !_traditionalPresenter.IsOpen) return false;
        InvalidateCompletion(); return true;
    }
}
