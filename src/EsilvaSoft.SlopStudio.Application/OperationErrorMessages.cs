using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EsilvaSoft.SlopStudio.Application;

/// <summary>Shared UI diagnostic categories. Logs contain type/category only, never document text or resolved credentials.</summary>
public static partial class OperationErrorMessages
{
    public static string Describe(Exception exception, bool export = false)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var types = new HashSet<string>(StringComparer.Ordinal);
        for (var type = exception.GetType(); type is not null; type = type.BaseType) types.Add(type.Name);
        var category = exception switch {
            OperationCanceledException => "Operação cancelada",
            TimeoutException => "Tempo limite excedido",
            JsonException => "JSON inválido",
            _ when types.Contains("MongoAuthenticationException") => "Falha de autenticação",
            _ when types.Contains("MongoExecutionTimeoutException") => "Tempo limite excedido",
            _ when types.Contains("MongoConnectionException") => "Falha de conexão",
            _ when types.Contains("MongoQueryException") || types.Contains("MongoCommandException") => "Erro de consulta MongoDB",
            _ when types.Contains("MongoException") => "Erro MongoDB",
            _ when export => "Erro de exportação",
            IOException or UnauthorizedAccessException => "Erro de arquivo ou armazenamento",
            FormatException or ArgumentException => "Entrada inválida",
            _ => "Operação não concluída"
        };
        Trace.WriteLine($"{category}; exception={exception.GetType().Name}; hresult={exception.HResult}", "Operações");
        // Network/driver failures can echo connection settings and server data. Keep those details out of the UI as well.
        if (types.Any(name => name.StartsWith("Mongo", StringComparison.Ordinal))) return category + ". Verifique o destino, as permissões e os parâmetros informados.";
        var message = exception.Message.Length > 2000 ? exception.Message[..2000] + "…" : exception.Message;
        return category + ": " + MongoUri().Replace(message, "[URI MongoDB protegida]");
    }

    [GeneratedRegex(@"mongodb(?:\+srv)?://[^\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MongoUri();
}
