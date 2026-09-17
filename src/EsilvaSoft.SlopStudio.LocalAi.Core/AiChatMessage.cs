namespace EsilvaSoft.SlopStudio.LocalAi.Core;

public sealed record AiChatMessage(string Role, string Content, DateTimeOffset CreatedAt)
{
    public string Author => Role == "user" ? "Você" : "Assistente IA";
}
