namespace EsilvaSoft.SlopStudio.LocalAi.Core;

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
