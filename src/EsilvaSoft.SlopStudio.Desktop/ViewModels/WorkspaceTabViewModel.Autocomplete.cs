using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class WorkspaceTabViewModel
{
    public Func<IReadOnlyList<EsilvaSoft.SlopStudio.Application.SyntaxHighlighting.SyntaxNamespace>> KnownSyntaxNamespaces { get; set; } = () => [];
    public void RefreshSyntaxContext() => OnPropertyChanged("SyntaxContext");
    public EsilvaSoft.SlopStudio.Application.SyntaxHighlighting.SyntaxContext CaptureSyntaxContext() =>
        new(KnownSyntaxNamespaces().Append(new(Profile?.Name ?? "", Database, Collection)).Distinct().ToArray(), Profile?.Name ?? "", Database, Collection);
    public Func<IReadOnlyList<string>> KnownAutocompleteNames { get; set; } = () => [];
    private string? _autocompleteResults;
    private string[] _autocompleteFields = [];
    public AutocompleteRequest CaptureAutocompleteRequest(string text, int caret)
    {
        var settings = Autocomplete.Settings;
        if (settings.UseResultPanelContext && !ReferenceEquals(_autocompleteResults, Results))
        {
            _autocompleteResults = Results;
            _autocompleteFields = MqlAutocompleteService.InferFieldPaths(ResultSegments.Take(8).Select(segment =>
                Results.Substring(segment.Start, Math.Min(segment.Length, 8192)))).Take(128).ToArray();
        }
        var fields = settings.UseResultPanelContext ? _autocompleteFields : [];
        var names = KnownAutocompleteNames().Concat(new[] { Profile?.Name ?? "", Database, Collection }).ToArray();
        var history = ConsoleHistory.Where(entry => entry.ProfileId == Profile?.Id && entry.Database == Database)
            .OrderByDescending(entry => entry.ExecutedAt).Take(3).Select(entry => entry.Script).ToArray();
        var language = Mode switch { "Agregação" => "json", "Script" => "JavaScript (mongosh)", _ => "Mongo Console JavaScript" };
        return AutocompleteContextBuilder.Build(new(text, caret, language,
            InputJson, fields, names, history, FilePath), settings);
    }
}
