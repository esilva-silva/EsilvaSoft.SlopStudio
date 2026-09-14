namespace EsilvaSoft.SlopStudio.Core;

public sealed record ConsoleRequest(ConnectionProfile Primary, string Database, string Script,
    int DocumentLimit = 100, int TimeoutMs = 30000, bool SaveHistory = true);

public sealed record ConsoleOperation(Guid ProfileId, string Database, string Collection, string Method, string ArgumentsJson);

public sealed record ConsoleWriteConfirmation(ConnectionProfile Profile, string Database, string Collection, string Method, string? FilterJson = null)
{
    public string Context => $"{Profile.Name} › {Database} › {Collection}\n{Profile.RoutingLabel}\nOperação: {Method}" + (FilterJson is null ? "" : "\nFiltro: " + FilterJson);
}

public sealed record ConsoleResultSet(int Number, string Json, Guid? ProfileId = null, string? Database = null,
    string? Collection = null, IReadOnlyList<string>? Documents = null, bool IsTruncated = false)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public ConnectionProfile? SourceProfile { get; init; }
    /// <summary>Collection method that produced the value (find, findOne, aggregate…), when known.</summary>
    public string? Method { get; init; }
    /// <summary>True when find/findOne received a non-empty projection, so documents may omit stored fields.</summary>
    public bool IsProjected { get; init; }
    public string DisplayText => $"[{Number}] {SourceProfile?.Name} › {Database}{(Collection is null ? "" : "." + Collection)} · {Documents?.Count ?? 0} documento(s){(IsTruncated ? " · limitado" : "")}";
}

public sealed record ConsoleExecutionResult(IReadOnlyList<ConsoleResultSet> Results, string Messages, string? Error,
    TimeSpan Duration, bool IsCanceled, IReadOnlyList<Guid> ConnectionsUsed, string Environment)
{
    public bool IsTimedOut { get; init; }
}

public sealed record ConsoleHistoryEntry(int Version, Guid Id, DateTimeOffset ExecutedAt, Guid ProfileId,
    string Connection, string Database, string Environment, string Script, double DurationMs, string Status,
    IReadOnlyList<Guid> ConnectionsUsed)
{
    public string? TargetHost { get; init; }
    public string DisplayText => $"{ExecutedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} · {Connection} › {Database} · {Environment} · {Status}";
}

public sealed record ConsoleStatement(int Start, int Length);
