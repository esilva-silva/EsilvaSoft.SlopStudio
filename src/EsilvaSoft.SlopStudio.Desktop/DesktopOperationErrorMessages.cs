using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.Desktop;

internal static class DesktopOperationErrorMessages
{
    public static string Describe(Exception exception, bool export = false) =>
        OperationErrorMessages.Describe(exception, export, LocalizationViewModel.Current.Resolve);
}
