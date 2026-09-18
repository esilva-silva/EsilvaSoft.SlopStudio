using EsilvaSoft.SlopStudio.Desktop.SyntaxHighlighting;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Context;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Text;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.Desktop;

/// <summary>Traditional (Ctrl+Space) completion list: presenter state, documentation preview and item acceptance.</summary>
public partial class WorkspaceTabView
{
    /// <summary>Status shown while a request is in flight; distinct from every other status text below.</summary>
    private const string TraditionalCompletionLoadingText = "Carregando sugestões…";
    /// <summary>Status shown when the provider fails; distinct from "no suggestions" (which means it succeeded with zero matches).</summary>
    private const string TraditionalCompletionErrorText = "Não foi possível carregar sugestões agora.";
    /// <summary>Status shown when there is no completion provider for this tab at all (feature unavailable, not "no matches").</summary>
    private const string TraditionalCompletionUnavailableText = "Sugestões tradicionais indisponíveis nesta aba.";

    private void ShowTraditionalCompletion(IEnumerable<CompletionItem> items, bool isIncomplete = false, CompletionContext? context = null)
    {
        _traditionalPresenter.Show(items);
        _traditionalCompletionDocument = CodeEditor.Text ?? "";
        _traditionalCompletionIncomplete = isIncomplete;
        _traditionalCompletionContext = context;
        TraditionalCompletionPanel.IsVisible = true;
        RefreshTraditionalCompletionList();
        PositionTraditionalCompletionPanel();
    }

    /// <summary>Visible feedback while the request is running; the presenter itself stays closed so a text change
    /// during this window still flows through the normal invalidate/reopen path instead of being swallowed as a refilter.</summary>
    private void ShowTraditionalCompletionLoading()
    {
        TraditionalCompletionPanel.IsVisible = true;
        TraditionalCompletionList.ItemsSource = Array.Empty<CompletionItem>();
        TraditionalCompletionList.SelectedItem = null;
        TraditionalCompletionStatus.Text = TraditionalCompletionLoadingText;
        if (this.FindControl<TextBlock>("TraditionalCompletionDocumentation") is { } documentation) documentation.Text = "";
        PositionTraditionalCompletionPanel();
    }

    private void ShowTraditionalCompletionMessage(string text)
    {
        _traditionalPresenter.Show([]);
        TraditionalCompletionPanel.IsVisible = true;
        TraditionalCompletionList.ItemsSource = Array.Empty<CompletionItem>();
        TraditionalCompletionList.SelectedItem = null;
        TraditionalCompletionStatus.Text = text;
        if (this.FindControl<TextBlock>("TraditionalCompletionDocumentation") is { } documentation) documentation.Text = "";
        PositionTraditionalCompletionPanel();
    }

    private void PositionTraditionalCompletionPanel()
    {
        var availableWidth = Math.Max(0, TraditionalCompletionLayer.Bounds.Width);
        var panelWidth = Math.Min(400, availableWidth);
        TraditionalCompletionPanel.Width = panelWidth > 0 ? panelWidth : 400;
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

    /// <summary>Immediately before the caret: the two characters this editor treats as automatic-open triggers.
    /// Reads the native, anchor-tracked CaretOffset instead of the cached CaretIndex wrapper, which a genuine
    /// keystroke can still be catching up to when the Text change notification for that same keystroke runs.</summary>
    private bool IsTraditionalTriggerCharacterAtCaret()
    {
        var text = CodeEditor.Text ?? "";
        var caret = Math.Clamp(CodeEditor.CaretOffset, 0, text.Length);
        return caret > 0 && text[caret - 1] is '.' or '$';
    }

    /// <summary>Re-evaluated one dispatcher tick later, once this keystroke's own property cascade has settled;
    /// see the call site in <c>WorkspaceTabView.Autocomplete.cs</c> for why it cannot run inline.</summary>
    private void ScheduleTraditionalTriggerCheck(WorkspaceTabViewModel tab) => Dispatcher.UIThread.Post(() =>
    {
        if (DataContext == tab && !_traditionalPresenter.IsOpen && CodeEditor.SelectionStart == CodeEditor.SelectionEnd
            && CodeEditor.IsKeyboardFocusWithin && IsTraditionalTriggerCharacterAtCaret())
            ShowTraditionalCompletionList(this, new RoutedEventArgs(), CompletionTrigger.TriggerCharacter);
    });

    private void CloseTraditionalCompletion()
    {
        _traditionalPresenter.Close();
        _traditionalDocumentationCancellation?.Cancel();
        _traditionalDocumentationCancellation = null;
        _traditionalDocumentationGeneration++;
        _traditionalCompletionDocument = null;
        _traditionalCompletionIncomplete = false;
        _traditionalCompletionContext = null;
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
        // Capturado antes de fechar a lista, que descarta o contexto: aceite e ranqueamento usam a mesma chave.
        if (DataContext is WorkspaceTabViewModel accepted)
        {
            accepted.RecordCompletionAccepted(_traditionalCompletionContext, item.SymbolId);
            _lastAcceptedCompletion = (_traditionalCompletionContext, item.SymbolId);
        }
        CloseTraditionalCompletion();
        if (placeholders is not null) BeginSnippet(start, placeholders);
        return true;
    }

    /// <summary>
    /// Último aceite desta aba, para que um desfazer imediato vire sinal negativo. Guarda apenas contexto e
    /// identificador de símbolo; a janela curta que caracteriza arrependimento é decidida pelo próprio rastreador.
    /// </summary>
    private (CompletionContext? Context, string SymbolId)? _lastAcceptedCompletion;

    private void RecordCompletionUndoneIfPending()
    {
        if (_lastAcceptedCompletion is not { } pending) return;
        _lastAcceptedCompletion = null;
        if (DataContext is WorkspaceTabViewModel tab) tab.RecordCompletionUndone(pending.Context, pending.SymbolId);
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

    /// <summary>XAML-bound entry point (Button.Click requires this exact delegate shape); always an explicit invocation.</summary>
    private void ShowTraditionalCompletionList(object? sender, RoutedEventArgs e) => ShowTraditionalCompletionList(sender, e, CompletionTrigger.Invoked);

    private async void ShowTraditionalCompletionList(object? sender, RoutedEventArgs e, CompletionTrigger trigger)
    {
        if (DataContext is not WorkspaceTabViewModel tab) return;
        if (CodeEditor.SelectionStart != CodeEditor.SelectionEnd) return;
        var selectedSymbol = _traditionalPresenter.Selected?.SymbolId;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        InvalidateCompletion();
        if (tab.TraditionalCompletion is null) { ShowTraditionalCompletionMessage(TraditionalCompletionUnavailableText); return; }
        AutocompleteMetrics.CompletionRequested.Add(1,
            new KeyValuePair<string, object?>("modality", "list"),
            new KeyValuePair<string, object?>("trigger", trigger == CompletionTrigger.Invoked ? "invoked" : "trigger-character"));
        var version = _completionSession.Version;
        var original = tab.Text;
        var caret = Math.Clamp(CodeEditor.CaretIndex, 0, original.Length);
        var mode = tab.Mode;
        var profile = tab.Profile; var database = tab.Database; var collection = tab.Collection;
        bool IsCurrent() => _completionSession.Version == version && CodeEditor.CaretIndex == caret && tab.Text == original
            && DataContext == tab && tab.Profile == profile && tab.Database == database && tab.Collection == collection && tab.Mode == mode;
        ShowTraditionalCompletionLoading();
        try
        {
            AutocompleteMetrics.UiDispatcherTime.Record(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("handler", "list"));
            // Owned per tab (EditorRequestScope), not a raw view-level CancellationTokenSource: superseding or
            // discarding this call never depends on which view instance happens to be bound right now.
            var traditional = await tab.GetTraditionalCompletionsAsync(new Language.Text.AvaloniaTextSnapshot(CodeEditor.Document), caret, CancellationToken.None, trigger);
            if (!IsCurrent()) return;
            ShowTraditionalCompletion(traditional.Items, traditional.IsIncomplete, traditional.Context);
            if (selectedSymbol is not null && _traditionalPresenter.Select(selectedSymbol) is not null) RefreshTraditionalCompletionList();
        }
        catch (OperationCanceledException) { if (IsCurrent()) CloseTraditionalCompletion(); }
        catch (Exception)
        {
            tab.Messages = "Sugestões tradicionais indisponíveis nesta solicitação.";
            if (IsCurrent()) ShowTraditionalCompletionMessage(TraditionalCompletionErrorText);
        }
    }
}
