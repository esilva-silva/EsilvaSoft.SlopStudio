using EsilvaSoft.SlopStudio.Desktop.SyntaxHighlighting;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Text;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.Desktop;

/// <summary>Traditional (Ctrl+Space) completion list: presenter state, documentation preview and item acceptance.</summary>
public partial class WorkspaceTabView
{
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
        if (this.FindControl<TextBlock>("TraditionalCompletionDocumentation") is { } documentation) documentation.Text = "";
        if (this.FindControl<Border>("TraditionalCompletionPanel") is { } panel) panel.IsVisible = false;
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

    private void TraditionalCompletionSelectionChanged(object? sender, SelectionChangedEventArgs e)
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

    private void AcceptTraditionalCompletion(object? sender, RoutedEventArgs e)
    {
        if (AcceptTraditionalCompletion()) e.Handled = true;
    }

    private async void ShowTraditionalCompletionList(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceTabViewModel tab) return;
        if (CodeEditor.SelectionStart != CodeEditor.SelectionEnd) return;
        var selectedSymbol = _traditionalPresenter.Selected?.SymbolId;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        InvalidateCompletion();
        AutocompleteMetrics.CompletionRequested.Add(1,
            new KeyValuePair<string, object?>("modality", "list"), new KeyValuePair<string, object?>("trigger", "invoked"));
        var version = _completionSession.Version;
        var original = tab.Text;
        var caret = Math.Clamp(CodeEditor.CaretIndex, 0, original.Length);
        var mode = tab.Mode;
        _completionCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(); _completionCancellation = cancellation;
        var profile = tab.Profile; var database = tab.Database; var collection = tab.Collection;
        bool IsCurrent() => _completionSession.Version == version && CodeEditor.CaretIndex == caret && tab.Text == original
            && DataContext == tab && tab.Profile == profile && tab.Database == database && tab.Collection == collection && tab.Mode == mode && !cancellation.IsCancellationRequested;
        try
        {
            AutocompleteMetrics.UiDispatcherTime.Record(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("handler", "list"));
            var traditional = await tab.GetTraditionalCompletionsAsync(new Language.Text.AvaloniaTextSnapshot(CodeEditor.Document), caret, cancellation.Token);
            if (!IsCurrent()) return;
            ShowTraditionalCompletion(traditional.Items, traditional.IsIncomplete);
            if (selectedSymbol is not null && _traditionalPresenter.Select(selectedSymbol) is not null) RefreshTraditionalCompletionList();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { tab.Messages = "Sugestões tradicionais indisponíveis nesta solicitação."; }
        finally { if (ReferenceEquals(_completionCancellation, cancellation)) _completionCancellation = null; }
    }
}
