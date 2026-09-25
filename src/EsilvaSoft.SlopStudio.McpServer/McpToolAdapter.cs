using System.Text;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application.Agents.Broker;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace EsilvaSoft.SlopStudio.McpServer;

/// <summary>
/// Translates MCP <c>tools/list</c> and <c>tools/call</c> to the broker contract. It holds no policy: names, schemas
/// and results come from the shared registry through the broker, and annotations are informative hints only. Domain
/// failures become tool results with a stable code; transport failures are explicit and never retried.
/// </summary>
internal sealed class McpToolAdapter(AgentBrokerClient broker)
{
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        ["list_connections"] = "Lista as conexões do Slop Studio autorizadas para este cliente (ID e nome; destinos externos recebem alias).",
        ["list_databases"] = "Lista os bancos autorizados de uma conexão, por ID lógico.",
        ["list_collections"] = "Lista as coleções autorizadas de um banco.",
        ["mongo_find"] = "Consulta somente leitura com filtro/projeção/ordenação em Extended JSON literal; até 100 documentos.",
        ["mongo_count"] = "Conta documentos com filtro em Extended JSON literal; o total volta como Extended JSON."
    };

    public async ValueTask<ListToolsResult> ListToolsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<AgentBrokerToolDescriptor> tools;
        try
        {
            tools = await broker.ListToolsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (AgentBrokerProtocolException exception)
        {
            throw new McpProtocolException(Describe(exception.Code), McpErrorCode.InternalError);
        }

        return new ListToolsResult
        {
            Tools = tools.Where(static tool => tool.ReadOnly && !tool.Destructive).Select(static tool => new Tool
            {
                Name = tool.Name,
                Description = Descriptions.GetValueOrDefault(tool.Name, "Tool somente leitura do Slop Studio."),
                InputSchema = tool.InputSchema,
                OutputSchema = tool.OutputSchema,
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false
                }
            }).ToList()
        };
    }

    public async ValueTask<CallToolResult> CallToolAsync(CallToolRequestParams? request, CancellationToken cancellationToken)
    {
        if (request?.Name is not { Length: > 0 } name)
            throw new McpProtocolException("Nome da tool ausente.", McpErrorCode.InvalidParams);
        if (!TryBuildArguments(request.Arguments, out var arguments))
            return Failure("InvalidArguments");

        AgentBrokerMessage result;
        try
        {
            result = await broker.CallToolAsync(name, arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (AgentBrokerProtocolException exception)
        {
            return Failure(exception.Code);
        }

        if (result.Status == AgentBrokerMessage.SucceededStatus && result.StructuredContent is { ValueKind: JsonValueKind.Object } content)
        {
            // Data only in structuredContent; the text block is a summary without documents (no duplication).
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = Summarize(name, content) }],
                StructuredContent = content,
                IsError = false
            };
        }

        if (result.ErrorCode == "UnknownTool")
            throw new McpProtocolException("Tool desconhecida ou não liberada: " + name, McpErrorCode.InvalidParams);
        return Failure(result.ErrorCode ?? AgentBrokerProtocol.ErrorCodes.PermissionDenied);
    }

    private static bool TryBuildArguments(IDictionary<string, JsonElement>? values, out JsonElement arguments)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in values ?? new Dictionary<string, JsonElement>())
            {
                writer.WritePropertyName(key);
                value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        arguments = default;
        if (buffer.Length > AgentBrokerProtocol.MaximumArgumentsBytes) return false;
        using var document = JsonDocument.Parse(buffer.ToArray());
        arguments = document.RootElement.Clone();
        return true;
    }

    private static string Summarize(string name, JsonElement content)
    {
        var builder = new StringBuilder("Resultado estruturado de ").Append(name);
        foreach (var property in content.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
                builder.Append("; ").Append(property.Name).Append(": ").Append(property.Value.GetArrayLength()).Append(" item(ns)");
            else if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                builder.Append("; ").Append(property.Name).Append(": ").Append(property.Value.GetBoolean() ? "sim" : "não");
        }
        return builder.Append(". Os dados estão em structuredContent.").ToString();
    }

    private static CallToolResult Failure(string code) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = code + ": " + Describe(code) }]
    };

    internal static string Describe(string code) => code switch
    {
        AgentBrokerProtocol.ErrorCodes.HostUnavailable =>
            "a IDE Slop Studio não está disponível (fechada, workspace ilegível ou broker desligado). A chamada não será repetida automaticamente; se já tinha sido enviada, o resultado é desconhecido.",
        AgentBrokerProtocol.ErrorCodes.AuthenticationRequired =>
            "o canal MCP não está autenticado (credencial ausente no cofre do sistema, revogada ou inválida). Registre o cliente novamente na IDE.",
        AgentBrokerProtocol.ErrorCodes.IncompatibleVersion =>
            "o proxy MCP e a IDE usam versões incompatíveis do protocolo local. Atualize ambos para a mesma versão.",
        AgentBrokerProtocol.ErrorCodes.RateLimited =>
            "muitas tentativas de autenticação inválidas; aguarde antes de tentar novamente.",
        AgentBrokerProtocol.ErrorCodes.PermissionDenied =>
            "operação não autorizada para este cliente. Conceda o acesso na IDE.",
        AgentBrokerProtocol.ErrorCodes.InvalidArguments => "argumentos inválidos para o schema da tool.",
        AgentBrokerProtocol.ErrorCodes.DeadlineExceeded => "prazo de execução excedido; nada foi repetido.",
        AgentBrokerProtocol.ErrorCodes.Cancelled => "chamada cancelada.",
        AgentBrokerProtocol.ErrorCodes.Busy => "limite de chamadas simultâneas atingido.",
        AgentBrokerProtocol.ErrorCodes.OutcomeUnknown => "resultado desconhecido; a chamada não será repetida.",
        "ResultTooLarge" => "resultado acima do limite; refine a consulta.",
        _ => "falha na chamada."
    };
}
