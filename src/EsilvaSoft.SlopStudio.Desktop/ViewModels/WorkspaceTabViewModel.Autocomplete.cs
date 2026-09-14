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
    public IReadOnlyList<string> GetObservedCompletionFields(string prefix)
    {
        if (!Autocomplete.Settings.UseResultPanelContext) return [];
        var target = IsAggregation ? new EsilvaSoft.SlopStudio.Application.SyntaxHighlighting.SyntaxNamespace(Profile?.Name ?? "", Database, Collection)
            : MongoCompletionTarget.Resolve(prefix, CaptureSyntaxContext());
        // A bare field prefix has no query path; use an unambiguous result source in this tab's current destination.
        if (target is null && prefix.All(c => char.IsLetterOrDigit(c) || c is '_' or '$'))
        {
            var origins = ResultSets.Select(set => set.Origin).Where(origin => origin.Profile == Profile && origin.Database == Database && origin.HasCollection)
                .Select(origin => new EsilvaSoft.SlopStudio.Application.SyntaxHighlighting.SyntaxNamespace(origin.Profile!.Name, origin.Database!, origin.Collection!)).Distinct().ToArray();
            if (origins.Length == 1) target = origins[0];
        }
        if (target is null || target.Collection.Length == 0) return [];
        var documents = ResultSets.Where(set => set.Origin.Profile?.Name == target.Connection && set.Origin.Database == target.Database &&
                set.Origin.Collection == target.Collection && (target.Connection != Profile?.Name || set.Origin.Profile == Profile))
            .SelectMany(set => set.Documents ?? []).Take(8).Select(document => document.Json).Where(json => json.Length <= 65536);
        return MqlAutocompleteService.InferFieldPaths(documents).Take(128).ToArray();
    }
    public AutocompleteRequest CaptureAutocompleteRequest(string text, int caret)
    {
        var settings = Autocomplete.Settings;
        var fields = GetObservedCompletionFields(text[..Math.Clamp(caret, 0, text.Length)]);
        var names = KnownAutocompleteNames().Concat(new[] { Profile?.Name ?? "", Database, Collection }).ToArray();
        var history = ConsoleHistory.Where(entry => entry.ProfileId == Profile?.Id && entry.Database == Database)
            .OrderByDescending(entry => entry.ExecutedAt).Take(3).Select(entry => entry.Script).ToArray();
        var language = Mode switch { "Agregação" => "json", "Script" => "JavaScript (mongosh)", _ => "Mongo Console JavaScript" };
        return AutocompleteContextBuilder.Build(new(text, caret, language,
            InputJson, fields, names, history, FilePath), settings);
    }
}
