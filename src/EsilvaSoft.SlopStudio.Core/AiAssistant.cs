namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Snapshot sent to an AI assistant. It is transient and is never part of a workspace snapshot.</summary>
public sealed record AiEditorContext(
    string Instruction,
    string Header,
    string EditorContent,
    string Language,
    string Dialect,
    string Database,
    string Collection,
    string OperationType,
    string AdditionalContext = "")
{
    public bool HasContext => !string.IsNullOrWhiteSpace(EditorContent)
        || !string.IsNullOrWhiteSpace(Database)
        || !string.IsNullOrWhiteSpace(Collection);

    public AiEditorContext Bounded() => this with
    {
        Instruction = Limit(Instruction, 4096),
        Header = Limit(Header, 512),
        EditorContent = Limit(EditorContent, 1_048_576),
        Language = Limit(Language, 64),
        Dialect = Limit(Dialect, 64),
        Database = Limit(Database, 256),
        Collection = Limit(Collection, 256),
        OperationType = Limit(OperationType, 64),
        AdditionalContext = Limit(AdditionalContext, 8192)
    };

    public AiEditorContext Validate()
    {
        if (string.IsNullOrWhiteSpace(Instruction)) throw new ArgumentException("A instrução da IA é obrigatória.", nameof(Instruction));
        if (string.IsNullOrWhiteSpace(Language)) throw new ArgumentException("A linguagem do editor é obrigatória.", nameof(Language));
        if (string.IsNullOrWhiteSpace(Dialect)) throw new ArgumentException("O dialeto do editor é obrigatório.", nameof(Dialect));
        return Bounded();
    }

    private static string Limit(string? value, int maximum) => (value ?? "")[..Math.Min(value?.Length ?? 0, maximum)];
}

public sealed record AiChatRequest(AiEditorContext Context);

/// <summary>Assistant output is a proposal only; it must not be executed or inserted implicitly.</summary>
public sealed record AiChatResponse(
    string Explanation,
    string ProposedCode,
    string Diff,
    bool RequiresAdditionalConfirmation = false,
    string Warning = "")
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Explanation) && string.IsNullOrWhiteSpace(ProposedCode);
}

public enum AiChatStatus { Idle, Loading, Ready, Error, Canceled, NoContext, Empty }

public sealed record AiChatMessage(string Role, string Content, DateTimeOffset CreatedAt)
{
    public string Author => Role == "user" ? "Você" : "Assistente IA";
}

/// <summary>Captures the exact editor version used to create a proposal.</summary>
public sealed record AiChangeProposal(
    Guid Id,
    string OriginalContent,
    string ProposedContent,
    string Explanation,
    string Diff,
    bool RequiresAdditionalConfirmation,
    string Warning,
    AiEditorContext Context)
{
    public bool IsNoOp => string.Equals(OriginalContent, ProposedContent, StringComparison.Ordinal);
}
