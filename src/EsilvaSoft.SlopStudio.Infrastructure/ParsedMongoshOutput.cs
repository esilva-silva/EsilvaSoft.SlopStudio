namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed record ParsedMongoshOutput(IReadOnlyList<string> Results, string ConsoleOutput);
