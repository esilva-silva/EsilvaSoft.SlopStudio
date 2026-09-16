using Avalonia;
using Avalonia.Headless;
using EsilvaSoft.SlopStudio.Desktop;

namespace EsilvaSoft.SlopStudio.UnitTests;

public static class UiTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().WithInterFont();
}
