using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <summary>One ephemeral row of the agent conversation. Nothing here is persisted.</summary>
public abstract class AgentChatItemViewModel : ObservableObject
{
    protected static LocalizationViewModel Text => LocalizationViewModel.Current;
}

public enum AgentChatRole
{
    User,
    Agent,
}

public sealed partial class AgentChatMessageItem : AgentChatItemViewModel
{
    public AgentChatMessageItem(AgentChatRole role, string text, AgentMessageId? messageId = null)
    {
        Role = role;
        MessageId = messageId;
        _content = text;
        _isStreaming = role == AgentChatRole.Agent && messageId is not null;
    }

    public AgentChatRole Role { get; }

    public AgentMessageId? MessageId { get; }

    public bool IsUser => Role == AgentChatRole.User;

    public string RoleLabel => Text.Resolve(IsUser ? "agentRoleUser" : "agentRoleAgent");

    /// <summary>Model text is displayed as data only; it never triggers commands, approvals or queries.</summary>
    [ObservableProperty] private string _content;

    [ObservableProperty] private bool _isStreaming;

    public string StreamingLabel { get; } = Text.Resolve("agentStreaming");

    internal void Append(string fragment) => Content += fragment;
}

public enum AgentToolCallState
{
    Requested,
    Running,
    Succeeded,
    Failed,
    Denied,
    Cancelled,
    OutcomeUnknown,
}

public sealed partial class AgentToolCallItem : AgentChatItemViewModel
{
    private long? _startedTimestamp;

    public AgentToolCallItem(AgentToolCallId callId, string? toolName, AgentDataDestinationKind? destination = null,
        AgentToolOrigin? origin = null, string? observedBy = null)
    {
        CallId = callId;
        ToolLabel = string.IsNullOrWhiteSpace(toolName) ? Text.Resolve("agentToolUnknown") : toolName;
        Destination = destination;
        Origin = origin;
        ObservedBy = observedBy;
    }

    /// <summary>Who ran the call, as published by the runtime (never inferred from provider output).</summary>
    public AgentToolOrigin? Origin { get; }

    /// <summary>Name of the executor of a provider-native tool (e.g. the official CLI), supplied by the host.</summary>
    public string? ObservedBy { get; }

    /// <summary>Native tool of the provider (display only): ran outside the registry, with no product approval.</summary>
    public bool IsProviderObserved => Origin == AgentToolOrigin.ProviderObserved;

    /// <summary>"Leitura nativa · executada pelo Claude Code, fora das ferramentas do KapibaraStudio" or the product-tool line.</summary>
    public string OriginText => Origin switch
    {
        AgentToolOrigin.ProviderObserved => Text.Format("agentToolOriginObserved",
            string.IsNullOrWhiteSpace(ObservedBy) ? Text.Resolve("agentToolOriginProvider") : ObservedBy, Branding.ProductName),
        AgentToolOrigin.Registry => Text.Format("agentToolOriginRegistry", Branding.ProductName),
        _ => "",
    };

    public bool HasOriginText => OriginText.Length > 0;

    /// <summary>Sanitized output destination published by the runtime (Local/External); null when not reported.</summary>
    public AgentDataDestinationKind? Destination { get; }

    /// <summary>"Destino: Externo" as text, never color alone; empty when the runtime did not report it.</summary>
    public string DestinationText => Destination switch
    {
        AgentDataDestinationKind.Local => Text.Format("agentToolDestination", Text.Resolve("agentDestinationLocal")),
        AgentDataDestinationKind.External => Text.Format("agentToolDestination", Text.Resolve("agentDestinationExternal")),
        _ => "",
    };

    public AgentToolCallId CallId { get; }

    /// <summary>Registry canonical name published by the runtime; raw model text is never echoed.</summary>
    public string ToolLabel { get; }

    public string Title => Text.Format(IsProviderObserved ? "agentToolNativeCard" : "agentToolCard", ToolLabel);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsError), nameof(IsTerminal), nameof(IsUncertain))]
    private AgentToolCallState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private long? _durationMilliseconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _errorCode;

    public bool IsError => State is AgentToolCallState.Failed or AgentToolCallState.Denied or AgentToolCallState.OutcomeUnknown;

    public bool IsUncertain => State == AgentToolCallState.OutcomeUnknown;

    public bool IsTerminal => State is not (AgentToolCallState.Requested or AgentToolCallState.Running);

    public string StatusText
    {
        get
        {
            var status = Text.Resolve(State switch
            {
                AgentToolCallState.Requested => "agentToolRequested",
                AgentToolCallState.Running => "agentToolRunning",
                AgentToolCallState.Succeeded => "agentToolSucceeded",
                AgentToolCallState.Failed => "agentToolFailed",
                AgentToolCallState.Denied => "agentToolDenied",
                AgentToolCallState.Cancelled => "agentToolCancelled",
                _ => "agentToolOutcomeUnknown",
            });
            if (DurationMilliseconds is { } duration)
            {
                status = Text.Format("agentToolStatusLine", status, Text.Format("agentToolDuration", duration));
            }

            // Known safe codes are described (4 languages); unknown ones keep the generic "code X" form.
            return ErrorCode is { Length: > 0 } code
                ? Text.Format("agentToolStatusLine", status, Text.HasTranslation(AgentChatViewModel.ErrorCodePrefix + code)
                    ? Text.Resolve(AgentChatViewModel.ErrorCodePrefix + code)
                    : Text.Format("agentToolCode", code))
                : status;
        }
    }

    internal void MarkStarted(long timestamp)
    {
        _startedTimestamp = timestamp;
        State = AgentToolCallState.Running;
    }

    internal void Complete(AgentToolCallState state, string? errorCode, TimeProvider clock)
    {
        if (_startedTimestamp is { } started)
        {
            DurationMilliseconds = (long)clock.GetElapsedTime(started).TotalMilliseconds;
        }

        ErrorCode = errorCode;
        State = state;
    }
}

public enum AgentApprovalCardState
{
    Pending,
    Granted,
    Denied,
    Expired,
    Cancelled,
}

public sealed partial class AgentApprovalCardItem : AgentChatItemViewModel
{
    public AgentApprovalCardItem(AgentApprovalViewModel approval) => Approval = approval;

    public AgentApprovalViewModel Approval { get; }

    public string Title { get; } = Text.Resolve("agentApprovalCardTitle");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsPending))]
    private AgentApprovalCardState _state;

    public bool IsPending => State == AgentApprovalCardState.Pending;

    public string StatusText => Text.Resolve(State switch
    {
        AgentApprovalCardState.Pending => "agentApprovalPending",
        AgentApprovalCardState.Granted => "agentApprovalGranted",
        AgentApprovalCardState.Denied => "agentApprovalDenied",
        AgentApprovalCardState.Expired => "agentApprovalExpiredCard",
        _ => "agentApprovalCancelledCard",
    });

    public string ReviewLabel { get; } = Text.Resolve("agentApprovalReview");
}

public sealed class AgentChatNoticeItem(string content, bool isError = false) : AgentChatItemViewModel
{
    public string Content { get; } = content;

    public bool IsError { get; } = isError;
}
