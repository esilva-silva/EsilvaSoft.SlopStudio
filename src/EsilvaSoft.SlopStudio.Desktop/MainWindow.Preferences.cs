using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace EsilvaSoft.SlopStudio.Desktop;

public partial class MainWindow
{
    /// <summary>Opens the owned preferences dialog and returns it for keyboard and rendering verification.</summary>
    public Window? ShowPreferences()
    {
        if (WorkspaceModel is not { } vm) return null;
        var window = new Window { Title = "Preferências", Width = 560, MinWidth = 460, MinHeight = 420, SizeToContent = SizeToContent.Height,
            MaxHeight = Math.Max(420, Math.Min(760, Bounds.Height - 40)), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = "Aparência e rascunhos", Classes = { "section-title" } });
        stack.Children.Add(new TextBlock { Text = "Tamanho da fonte do editor (12–20)" });
        var size = new NumericUpDown { Minimum = 12, Maximum = 20, Value = (decimal)vm.CodeFontSize };
        size.ValueChanged += (_, _) => vm.CodeFontSize = (double)(size.Value ?? 14);
        stack.Children.Add(size);
        var recover = new CheckBox { Content = "Recuperar rascunhos ao reabrir", IsChecked = vm.RecoverDrafts };
        recover.IsCheckedChanged += (_, _) => vm.RecoverDrafts = recover.IsChecked == true;
        stack.Children.Add(recover);
        var profile = new CheckBox { Content = "Recuperar rascunhos desta conexão", IsChecked = vm.RecoverActiveProfile, IsEnabled = vm.CanSetProfileRecovery };
        profile.IsCheckedChanged += (_, _) => vm.RecoverActiveProfile = profile.IsChecked == true;
        stack.Children.Add(profile);
        vm.IdentifierPreferences.Load(vm.IdentifierMode);
        stack.Children.Add(new IdentifierModePanel { Name = "GlobalIdentifierPanel", DataContext = vm.IdentifierPreferences });
        vm.UuidPreferences.Load(vm.UuidRepresentation);
        vm.UuidPreferences.IsPreviewVisible = vm.IdentifierMode != Core.IdentifierRepresentationMode.ObjectId;
        stack.Children.Add(new UuidRepresentationPanel { Name = "GlobalUuidPanel", DataContext = vm.UuidPreferences });
        var autocomplete = new Button { Content = "Autocomplete…" };
        autocomplete.Click += (_, _) =>
        {
            vm.AutocompletePreferences.Load(vm.AutocompleteService.Settings);
            var preferences = new AutocompleteSettingsWindow { DataContext = vm.AutocompletePreferences, Height = Math.Min(680, Bounds.Height - 40) };
            _ = preferences.ShowDialog(window);
        };
        stack.Children.Add(autocomplete);
        stack.Children.Add(new TextBlock { Text = "O texto das abas é salvo localmente e pode conter dados sensíveis. Resultados e credenciais da conexão não são recuperados. A entrada JSON só é salva quando você habilita essa opção na aba.", TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
        var close = new Button { Content = "Concluir", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += async (_, _) =>
        {
            try { await vm.SaveSessionAsync(); window.Close(); }
            catch (Exception ex) { await Dialogs.ChooseAsync(window, "Preferências não salvas", ex.Message, "Fechar"); }
        };
        stack.Children.Add(close);
        window.Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.KeyDown += (_, args) => { if (args.Key == Key.Escape) { args.Handled = true; window.Close(); } };
        _ = window.ShowDialog(this);
        return window;
    }
}
