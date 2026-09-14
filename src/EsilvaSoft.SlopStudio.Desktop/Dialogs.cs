using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace EsilvaSoft.SlopStudio.Desktop;

internal static class Dialogs
{
    public static Task<string?> ChooseAsync(Window owner, string title, string message, params string[] choices)
        => ChooseCancelableAsync(owner, title, message, CancellationToken.None, choices);
    public static async Task<string?> ChooseCancelableAsync(Window owner, string title, string message, CancellationToken cancellationToken, params string[] choices)
    {
        var window = new Window { Title = title, Width = 460, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        stack.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var choice in choices)
        {
            var button = new Button { Content = choice };
            button.Click += (_, _) => window.Close(choice);
            buttons.Children.Add(button);
        }
        stack.Children.Add(buttons); window.Content = stack;
        window.Opened += (_, _) => buttons.Children.LastOrDefault()?.Focus();
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; window.Close(null); } };
        cancellationToken.ThrowIfCancellationRequested();
        var result = window.ShowDialog<string?>(owner);
        using var registration = cancellationToken.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => window.Close(null)));
        return await result;
    }
}
