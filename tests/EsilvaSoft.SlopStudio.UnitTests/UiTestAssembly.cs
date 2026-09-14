using Avalonia.Headless;
using EsilvaSoft.SlopStudio.UnitTests;

// AvaloniaEdit's command bindings have UI-thread affinity. Use the supported assembly session
// so every UI test runs on the same dispatcher, as in the desktop application.
[assembly: AvaloniaTestApplication(typeof(UiTestApp))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
