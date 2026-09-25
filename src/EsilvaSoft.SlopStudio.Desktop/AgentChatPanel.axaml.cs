using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.Desktop;

/// <summary>
/// Native agent chat of one tab. The view owns focus, scrolling and dialogs; the view model owns state and the runtime.
/// Ctrl+Enter acts only while the composer has focus; Escape there discards the preview and never cancels a turn.
/// Streaming updates neither move focus nor scroll away from a message the user is reading.
/// </summary>
public partial class AgentChatPanel : UserControl
{
    private const double StickThreshold = 8;
    private AgentChatViewModel? _viewModel;
    private ScrollViewer? _historyScroll;
    private bool _stickToBottom = true;

    public AgentChatPanel()
    {
        InitializeComponent();
        Composer.AddHandler(KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => Attach(DataContext as AgentChatViewModel);
        History.TemplateApplied += (_, _) => AttachHistoryScroll();
        History.LayoutUpdated += (_, _) => AttachHistoryScroll();
    }

    /// <summary>The approval dialog currently open from this panel, if any.</summary>
    public AgentApprovalWindow? OpenApprovalWindow { get; private set; }

    /// <summary>The settings dialog currently open from this panel, if any.</summary>
    public AgentSettingsWindow? OpenSettingsWindow { get; private set; }

    public TextBox ComposerBox => Composer;

    private void Attach(AgentChatViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.ApprovalRequested -= OnApprovalRequested;
            _viewModel.SettingsRequested -= OnSettingsRequested;
            _viewModel.ComposerFocusRequested -= OnComposerFocusRequested;
        }

        _viewModel = viewModel;
        if (viewModel is not null)
        {
            viewModel.ApprovalRequested += OnApprovalRequested;
            viewModel.SettingsRequested += OnSettingsRequested;
            viewModel.ComposerFocusRequested += OnComposerFocusRequested;
        }
    }

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Plain Enter inserts a line; only Ctrl+Enter in this scope reviews or sends.
            e.Handled = true;
            if (_viewModel.PrimaryActionCommand.CanExecute(null))
            {
                _ = _viewModel.PrimaryActionCommand.ExecuteAsync(null);
            }
        }
        else if (e.Key == Key.Escape && _viewModel.HasPreview)
        {
            e.Handled = true;
            _viewModel.EditCommand.Execute(null);
        }
    }

    private void OnComposerFocusRequested(object? sender, EventArgs e) => Composer.Focus();

    private void AttachHistoryScroll()
    {
        if (_historyScroll is not null)
        {
            return;
        }

        _historyScroll = History.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_historyScroll is not null)
        {
            _historyScroll.ScrollChanged += OnHistoryScrollChanged;
        }
    }

    private void OnHistoryScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_historyScroll is not { } scroll)
        {
            return;
        }

        if (e.ExtentDelta.Y != 0 && e.OffsetDelta.Y == 0)
        {
            // New content: follow the stream only if the reader was already at the end.
            if (_stickToBottom)
            {
                scroll.Offset = new Vector(scroll.Offset.X, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
            }

            return;
        }

        _stickToBottom = scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - StickThreshold;
    }

    private void OnApprovalRequested(object? sender, AgentApprovalViewModel approval) => _ = ShowApprovalAsync(approval);

    private async Task ShowApprovalAsync(AgentApprovalViewModel approval)
    {
        if (OpenApprovalWindow is not null)
        {
            return; // One modal at a time; the card keeps "Revisar aprovação…" for the others.
        }

        var window = new AgentApprovalWindow { DataContext = approval };
        OpenApprovalWindow = window;
        try
        {
            if (TopLevel.GetTopLevel(this) is Window owner && owner.IsVisible)
            {
                await window.ShowDialog(owner);
            }
            else
            {
                var closed = new TaskCompletionSource();
                window.Closed += (_, _) => closed.TrySetResult();
                window.Show();
                await closed.Task;
            }
        }
        catch (Exception)
        {
            // A dialog that cannot open leaves the approval pending; the runtime denies it on expiry.
        }
        finally
        {
            OpenApprovalWindow = null;
            Composer.Focus();
        }
    }

    private void OnSettingsRequested(object? sender, EventArgs e) => _ = ShowSettingsAsync();

    private async Task ShowSettingsAsync()
    {
        if (_viewModel is null || OpenSettingsWindow is not null)
        {
            return;
        }

        var window = new AgentSettingsWindow { DataContext = _viewModel.CreateSettingsViewModel() };
        OpenSettingsWindow = window;
        try
        {
            if (TopLevel.GetTopLevel(this) is Window owner && owner.IsVisible)
            {
                await window.ShowDialog(owner);
            }
            else
            {
                var closed = new TaskCompletionSource();
                window.Closed += (_, _) => closed.TrySetResult();
                window.Show();
                await closed.Task;
            }
        }
        catch (Exception)
        {
            // Nothing changed; the chat keeps its previous provider state.
        }
        finally
        {
            OpenSettingsWindow = null;
            _viewModel?.ReloadProviders();
            ConfigureButton.Focus();
        }
    }
}
