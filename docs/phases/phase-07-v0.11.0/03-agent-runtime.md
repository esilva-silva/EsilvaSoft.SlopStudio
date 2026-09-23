# Agent Runtime — contrato interno proposto

Estado: **planejado**. Os exemplos C# são especificação para revisão, não arquivos compiláveis adicionados à solução. `IAiChatService` atual permanece compatível durante a adoção. Ver [análise de código](13-analise-do-codigo.md) e [arquitetura](02-arquitetura.md).

## Portas e responsabilidades

```csharp
public interface IAgentProvider
{
    AgentProviderDescriptor Describe();
    Task<IAgentSession> CreateSessionAsync(
        AgentSessionOptions options, CancellationToken cancellationToken);
}

public interface IAgentSession : IAsyncDisposable
{
    AgentSessionId Id { get; }
    IAsyncEnumerable<AgentEvent> RunTurnAsync(
        AgentTurnRequest request, CancellationToken cancellationToken);
    Task SubmitToolResultAsync(
        AgentToolResult result, CancellationToken cancellationToken);
    Task SubmitApprovalAsync(
        AgentApprovalDecision decision, CancellationToken cancellationToken);
    Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken);
}

public interface IAgentRuntime
{
    Task<AgentSessionId> StartSessionAsync(
        AgentSessionOptions options, CancellationToken cancellationToken);
    IAsyncEnumerable<AgentEvent> RunTurnAsync(
        AgentSessionId sessionId, AgentTurnRequest request,
        CancellationToken cancellationToken);
    Task DecideApprovalAsync(
        AgentApprovalDecision decision, CancellationToken cancellationToken);
    Task CancelTurnAsync(AgentSessionId sessionId, AgentTurnId turnId,
        CancellationToken cancellationToken);
    Task CloseSessionAsync(AgentSessionId sessionId, CancellationToken cancellationToken);
}

public interface IAgentToolRegistry
{
    IReadOnlyList<AgentToolDescriptor> List();
    Task<AgentToolResult> ExecuteAsync(AgentToolInvocation invocation,
        CancellationToken cancellationToken);
}
```

Interfaces ficam em `Application/Agents`, DTOs puros em `Core/Agents`. `IAgentPermissionService.EvaluateAsync` retorna allow/deny com código, escopo e revisão; `IAgentApprovalService.RequestAsync` registra uma solicitação e aguarda decisão autenticada com expiração; `IAgentContextProvider.CaptureAsync` produz snapshot conforme consentimento; `IAgentCredentialProvider.ResolveAsync` entrega lease de credencial **apenas ao adapter confiável**, nunca ao runtime de eventos, à UI ou às tools. Este último compõe `ISecretStore`, sem ser um segundo armazenamento.

O runtime é o único consumidor da sessão do provider e o único publicador do stream normalizado para a UI. `List()` retorna descritores estáticos sem dados privados; conceder acesso depende de `ExecuteAsync`, não da descoberta. `SubmitToolResultAsync` desbloqueia o adapter enquanto `RunTurnAsync` continua enumerando; é obrigatório que esse caminho seja concorrente e não adquira o mesmo lock da leitura do stream. Uma ferramenta resultante de MCP já executada pelo broker produz somente eventos de observação no adapter: nunca executar novamente porque o provider reportou a mesma chamada.

## DTOs e identidade

`AgentSessionOptions` contém `ProviderId`, `ModelId` opcional, configurações sem secrets e capacidades solicitadas. Não contém perfil Mongo completo. `AgentTurnRequest` contém `TurnId`, mensagem do usuário, snapshot autorizado, identificador de aba e versão do documento; prompts não recebem contexto implícito do explorer.

`AgentToolInvocation` é criado pelo runtime/broker: `RequestId`, `SessionId`, `TurnId`, `ToolCallId`, `AgentPrincipal`, `ToolName`, `SchemaVersion`, `Arguments`, `TargetSnapshot`, `PolicyRevision`, `DeadlineUtc`. O principal autenticado e a autorização não vêm dos argumentos JSON enviados pelo agente. `TargetSnapshot` tem ID lógico de conexão, revisão, banco, coleção e representação UUID; credenciais resolvidas são objetos privados do executor.

`AgentToolResult` contém `ToolCallId`, `Status`, `Data`, `ErrorCode`, `SafeMessage`, `Truncated`, contagens/duração e `OutcomeCertainty`. Nenhum objeto nativo de SDK, driver ou exceção bruta atravessa esta fronteira. `RequestId` repetido no mesmo principal com argumentos distintos retorna conflito; repetido com mesmos argumentos retorna estado conhecido da execução, sem novo despacho. Isso é deduplicação local, não promessa de exactly-once MongoDB após crash.

## Eventos normalizados e regras do stream

Cada evento tem `ProtocolVersion = 1`, `EventId`, `SessionId`, `TurnId` opcional, `Sequence` monotônico por sessão, `TimestampUtc`, `CorrelationId` e payload tipado. IDs nativos ficam no adapter e em diagnósticos saneados, não viram protocolo público. `AgentEvent` pode ser uma hierarquia fechada de records; não há necessidade de `IAgentEvent` sem comportamento.

| Evento | Payload e invariante |
| --- | --- |
| `SessionStarted`, `SessionCompleted` | Provider/modelo efetivos, capabilities; término inclui motivo |
| `MessageStarted`, `MessageDelta`, `MessageCompleted` | `MessageId`, papel, fragmento e estado; delta nunca aplica código |
| `ToolRequested`, `ToolStarted`, `ToolCompleted`, `ToolFailed` | Call ID, tool, alvo saneado, risco, resumo/status; requested não significa autorizado |
| `ApprovalRequested`, `ApprovalGranted`, `ApprovalDenied` | Approval ID, hash, escopo, expiração e decisão humana; granted não vem de texto do modelo |
| `FileChangeProposed`, `DatabaseChangeProposed` | Proposta imutável, destino e diff permitido; nenhum efeito automático |
| `TaskStarted`, `TaskProgress`, `TaskCompleted` | Turn ID, fase, progresso opcional, status terminal e certeza |
| `AgentError` | Categoria, código, mensagem saneada, possibilidade de retry; sem prompt/URI/stack nativa |

Um terminal por mensagem/tool/turno. Evento tardio de turno cancelado não reabre o turno nem atualiza outra aba. Runtime controla sequência e descarta duplicados do adapter. Eventos desconhecidos de provider são ignorados com diagnóstico saneado se opcionais; protocolo incompatível que afeta autorização interrompe a sessão. Respostas de erro, tool results e texto de documentos são dados não confiáveis, jamais instruções para ampliar permissões.

Stream usa fila limitada proposta de 256 eventos/1 MiB por sessão; agregar deltas textuais por até 50 ms para reduzir pressão de UI. Não descartar aprovações ou eventos terminais. Se consumidor parar, cancelar com `ConsumerUnavailable` após prazo; não acumular texto indefinidamente. Valores são limites iniciais de produto a validar por benchmark. Retenção em memória: 100 mensagens ou 2 MiB, com aviso antes de reduzir contexto; resumo não ganha autorização para ler novos dados.

## Máquinas de estado

```text
Sessão: Created -> Starting -> Ready <-> Running -> Closing -> Closed
                   |             |        |
                   +-> Failed    +-> Unavailable <-> Ready

Turno: Queued -> Running <-> WaitingTool / WaitingApproval
                    |                 |
                    +----> CancelRequested -> Cancelled / OutcomeUnknown
                    +----> Completed / Failed / TimedOut / Interrupted

Tool: Requested -> Validated -> Authorized -> AwaitingApproval (quando exigida)
        |             |            |                |
        +-> Rejected  +-> Denied   +--------------> Approved
                                                     |
                           AuditedIntent -> Dispatched -> Succeeded / Failed
                                                     -> Cancelled / OutcomeUnknown
```

Uma sessão aceita um turno ativo; mensagens adicionais ficam em fila curta (máximo proposto 3) ou são recusadas explicitamente. Sessões distintas são concorrentes; orçamento inicial de tools: 2 por sessão, 4 por conexão e 8 globais, reduzível por política. Escritas no mesmo namespace são serializadas no broker. `WaitingApproval` não segura conexão, lock de LiteDB ou slot de execução Mongo.

Cancelamento tem dois caminhos: sinalizar CTS específica e enviar interrupção oficial ao provider. Se o adapter não confirmar parada no prazo proposto de 5 s, fechar seu processo/canal isolado e reportar interrupção. Operação já enviada ao MongoDB pode ter sido aplicada; retornar `OutcomeUnknown`, nunca rollback. Cancelamento antes do despacho é `Cancelled` sem efeito. Separar prazo de aprovação (120 s) de execução MongoDB (padrão 5 s, teto 30 s); `DeadlineUtc` total da chamada não excede 155 s incluindo até 5 s de preparação/fila. `maxTimeMS` começa no despacho e não pode exceder prazo restante. O turno tem orçamento próprio configurado e pode encerrar antes desses tetos. Não repetir escrita automaticamente por timeout/reconexão.

Ao mudar conexão, modelo, grant ou conta durante aprovação, invalidar hash/revisão e pedir nova ação explícita. Mudança do explorer não muda sessão. Reinício invalida tokens de aprovação, marca turnos incompletos e não reenvia comandos. Continuidade de conversa utiliza recurso oficial do provider quando capability existe, com aviso de que contexto já enviado pode persistir no serviço; trocar provider inicia sessão nova e só transfere conteúdo escolhido pelo usuário.

## Capabilities e degradação

`AgentCapabilities` declara `Chat`, `Streaming`, `ToolCalling`, `Mcp`, `Sessions`, `FileEditing`, `CommandExecution`, `SubAgents`, `ThinkingSummary`, `ModelSelection` e métodos oficiais de autenticação. Capacidades efetivas são a interseção de adapter testado, versão disponível, modelo, autenticação e política. UI reage a capabilities, sem condicionais por marca. `ThinkingSummary` é um resumo disponibilizado oficialmente, não cadeia de raciocínio privada.

`FileEditing`, `CommandExecution` e `SubAgents` ficam **desabilitadas na baseline**, mesmo que provider as ofereça: não criar via lateral para acessar `.env`, workspace LiteDB ou executar mongosh livre. Propostas editáveis podem aparecer sem capacidade de execução. Providers novos implementam as portas, tradução, descrição e testes de conformidade; registro DI/configuração basta para aparecer na mesma UI. Copilot é candidato futuro, sem prometer SDK/login não verificado.

## Preservação da IA local

`LocalAgentProvider` em Application adapta `ILocalAiModelService.StreamAsync`, suas capacidades, prioridades e descarte; não cria segundo proprietário de modelo. Não chamar `CancelGeneration()` global para cancelar uma sessão: usar token da solicitação e a semântica de preempção existente. Autocomplete mantém `LoadedOnly`, e abrir o chat não descarrega automaticamente o modelo em uso.

O atual `LocalModelAiChatService` é proposta de código FIM, e `AiChatService` é fallback determinístico. Não declarar tool calling, sessões remotas ou agente autônomo ONNX como existentes. Um modelo local só anuncia `Chat`/`Streaming` conforme adaptador/modelo comprovado; tool calling fica falso até protocolo estruturado validado e testes próprios. Assistente de proposta existente e autocomplete funcionam offline independentemente de providers externos.

## Aceite do contrato

Testes precisam provar: fluxo com tool e aprovação sem deadlock, IDs duplicados, evento tardio, cancelamento de A sem cancelar B, provider morto durante streaming, troca de perfil durante aprovação, reconexão sem replay de escrita, fila cheia, expiração de credencial e nenhuma fuga de secrets em todos os eventos. Dois adapters e local passam pelo mesmo consumidor de UI e pelo mesmo registry. Ver [plano de testes](11-plano-de-testes.md).
