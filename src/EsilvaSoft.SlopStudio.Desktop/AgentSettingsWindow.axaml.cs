using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.Desktop;

/// <summary>Provider settings. Opening it lists capabilities only; it never starts authentication.</summary>
public partial class AgentSettingsWindow : Window
{
    public AgentSettingsWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            if (ProviderList.IsVisible)
            {
                ProviderList.Focus();
            }
            else
            {
                CloseButton.Focus();
            }
        };
        // The typed key never outlives the window.
        Closed += (_, _) =>
        {
            if (DataContext is AgentSettingsViewModel model)
            {
                model.ApiKey = "";
            }
        };
    }

    private void CloseDialog(object? sender, RoutedEventArgs e) => Close();
}
