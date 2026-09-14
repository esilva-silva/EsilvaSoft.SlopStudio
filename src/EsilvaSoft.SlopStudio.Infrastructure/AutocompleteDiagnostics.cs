using System.Diagnostics;
using EsilvaSoft.SlopStudio.Application;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Local technical diagnostics. Callers must never pass code, prompts or raw native exceptions.</summary>
public sealed class AutocompleteDiagnostics : IAutocompleteDiagnostics
{
    public void Record(string eventName, string? detail = null, TimeSpan? duration = null) =>
        Trace.WriteLine($"{eventName} {detail} {(duration is { } elapsed ? elapsed.TotalMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " ms" : "")}", "Autocomplete");
}
