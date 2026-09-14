using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

/// <summary>One instance per editor. A monotonically increasing version also rejects edit/undo ABA responses.</summary>
public sealed class CompletionSession : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private long _version;
    private bool _disposed;
    public long Version { get { lock (_gate) return _version; } }

    public void Invalidate()
    {
        lock (_gate) { _version++; _pending?.Cancel(); }
    }

    public async Task<AutocompleteResult?> RequestAsync(IAutocompleteService service, AutocompleteRequest request, bool immediate = false)
    {
        ArgumentNullException.ThrowIfNull(service);
        using var cancellation = new CancellationTokenSource();
        long version;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _pending?.Cancel(); _pending = cancellation; version = ++_version;
        }
        try
        {
            if (service.GetImmediateCompletion(request) is { } dictionary)
            {
                lock (_gate) return !_disposed && version == _version && !cancellation.IsCancellationRequested ? dictionary : null;
            }
            if (!immediate) await Task.Delay(service.Settings.DelayMilliseconds, cancellation.Token).ConfigureAwait(false);
            var result = await service.GetCompletionAsync(request, cancellation.Token).ConfigureAwait(false);
            lock (_gate) return !_disposed && version == _version && !cancellation.IsCancellationRequested ? result : null;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return null; }
        finally { lock (_gate) if (ReferenceEquals(_pending, cancellation)) _pending = null; }
    }

    public void Dispose() { lock (_gate) { _disposed = true; _version++; _pending?.Cancel(); } }
}
