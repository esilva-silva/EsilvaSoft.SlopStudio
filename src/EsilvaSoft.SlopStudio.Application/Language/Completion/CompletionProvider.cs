using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Language.Completion;

public interface ICompletionProfileResolver
{
    /// <summary>Resolves a transient driver profile from identity; the result must not be retained in request snapshots.</summary>
    ConnectionProfile? Resolve(Guid profileId);
}

public enum CompletionProviderKind : byte { Traditional, Ai, TraditionalPreemptive, AiPreemptive }

/// <summary>Identity captured with the request; a response is never valid for another stamp.</summary>
public sealed record CompletionRequest
{
    public CompletionRequest(CompletionContext context, long requestId, int presentationGeneration)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestId);
        ArgumentOutOfRangeException.ThrowIfNegative(presentationGeneration);
        Context = context;
        RequestId = requestId;
        PresentationGeneration = presentationGeneration;
    }

    public CompletionContext Context { get; }
    public long RequestId { get; }
    public int PresentationGeneration { get; }
}

public sealed record CompletionResponse(CompletionRequest Request, CompletionList List)
{
    public bool IsFor(CompletionRequest request) => Request.RequestId == request.RequestId
        && Request.PresentationGeneration == request.PresentationGeneration
        && Request.Context.Version == request.Context.Version;
}

public interface ICompletionProvider
{
    CompletionProviderKind Kind { get; }
    ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default);
}

public sealed class TraditionalCompletionProvider(CompletionService service) : ICompletionProvider
{
    private readonly CompletionService _service = service ?? throw new ArgumentNullException(nameof(service));
    public CompletionProviderKind Kind => CompletionProviderKind.Traditional;

    public async ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var list = await _service.CompleteAsync(request.Context, cancellationToken).ConfigureAwait(false);
        return new(request, list);
    }
}
