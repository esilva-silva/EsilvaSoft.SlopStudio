# Persistência de concessões de agentes

Estado em 23/09/2026: faceta interna implementada e registrada em DI no proprietário LiteDB existente como `IAgentAuthorizationPolicyProvider`/`IAgentAuthorizationPolicyRepository`; `IAgentPermissionEvaluator` também está composto. A prova de composição confirma que ambas as facetas compartilham o mesmo singleton de `LiteDbConnectionProfileRepository`; `IAgentToolRegistry` permanece deliberadamente fora do DI enquanto faltam broker/identidade/auditoria e os gates de saída. Ainda não há UI de concessões, runtime, auditoria de aprovações ou exposição MCP. A persistência passou 27 testes Windows e a composição/autorização/registry passou 52 testes focados Windows em 23/09/2026. O contrato não concede acesso por si só e não conclui o Lote 2 nem qualquer AC da fase.

## Propriedade e migração

`LiteDbConnectionProfileRepository` implementa `IAgentAuthorizationPolicyRepository`, que estende o provider de leitura. A nova faceta usa exclusivamente `_database`, `_gate` e `RunAsync` do proprietário. Não cria conexão, arquivo ou proprietário adicional. I/O ocorre no worker existente; leitura, comparação de revisão e gravação são síncronas sob o mesmo cadeado. Uma única operação `Upsert` grava todo o snapshot; não há transação atravessando `await` ou MongoDB.

A migração é aditiva: coleção `agentAuthorizationPolicies`, um documento por principal, versão de schema 1 explícita. Nenhum documento de perfil, sessão, ambiente ou histórico é convertido. Workspace sem política retorna ausência, que o evaluator nega; leitura não insere concessões/defaults. O primeiro salvamento explícito cria revisão 1. Versão desconhecida é rejeitada, sem tentativa de downgrade ou normalização destrutiva.

## Schema fechado

| Campo | Contrato |
| --- | --- |
| `_id` | GUID não vazio do principal |
| `schemaVersion` | Inteiro de 32 bits, exatamente 1 |
| `revision` | Inteiro de 64 bits, positivo e monotônico por principal |
| `updatedAtUtc` | Timestamp do salvamento |
| `grants` | Array com até 1.024 concessões explícitas |

Cada concessão contém `principalId`, `invocationKind`, `sessionId`, `turnId`, `sourceGenerationId`, `permission`, `connectionId`, `databaseName`, `collectionName`, `destinationKind`, `providerId` e `outputDataScope`. Campos opcionais são representados por `null` explícito; omitir um campo nunca amplia seu escopo. `Session` exige turno nulo; `Turn` exige GUID de turno válido. Coleção exige banco. Destino local exige provider nulo; destino externo exige provider válido. Todos os enums, GUIDs, nomes, combinações e duplicatas são validados antes de publicar o snapshot.

Campos desconhecidos, tipos inesperados, versão desconhecida, duplicatas ou principal divergente invalidam o documento inteiro. Não se recupera o subconjunto aparentemente válido de uma política corrompida. O documento armazena apenas identidade e metadados de autorização; não recebe perfil completo, URI, host, credenciais, argumentos de query ou resultados MongoDB.

## Concorrência, falha e recuperação

`SaveAsync` recebe `expectedRevision`: zero significa somente política ausente. O repositório atribui `expectedRevision + 1` e copia as concessões antes do primeiro trabalho assíncrono. A comparação ocorre após reler e validar o documento sob `_gate`. Dois salvamentos com a mesma revisão têm no máximo um vencedor; o outro recebe `AgentPolicyConcurrencyException` e precisa recarregar/revisar a decisão. Não existe retry automático com uma revisão atualizada.

Revogar todas as concessões grava uma lista vazia em uma nova revisão; não apaga a política nem reinicia seu contador. Revisão esgotada (`long.MaxValue`) impede outra gravação. A garantia cobre operações deste proprietário; não é proteção criptográfica contra edição externa ou restauração de backup antigo.

Falha de leitura, documento inválido e falha de gravação são observáveis no contrato. O evaluator converte indisponibilidade em negação. Antes de qualquer substituição, inclusive por grants vazios, o repositório relê o documento: corrupção ou versão desconhecida impedem o `Upsert`, preservando os dados originais. Cancelamento é verificado novamente após entrar no cadeado e antes da gravação; não promete desfazer gravação já concluída.

Não existe limpeza, reparação ou recriação automática. A recuperação operacional continua dependendo do fluxo de backup/reparo do workspace com seu proprietário fechado. Corrigir uma causa transitória permite nova leitura; escrever novamente ainda exige revisão correta. A infraestrutura de UI/diagnóstico e o fluxo de recuperação do produto continuam pendentes de integração.

## Evidência

`LiteDbAgentAuthorizationPolicyTests`: 27 testes aprovados em 23/09/2026, sem falhas ou ignorados, com build das dependências. Cobrem persistência entre reaberturas, preservação das outras coleções, ausência de dados do perfil no documento de concessões, autorização pelo evaluator real, 20 corrupções de schema/concessão, preservação integral do documento ilegível, revogação, 12 escritores concorrentes, cópia de entrada mutável, cancelamento, proprietário fechado e rejeição de escrita pelo LiteDB seguida de recuperação. A injeção offline abre o banco apenas depois de fechar o proprietário anterior.

Comando: `dotnet test tests/EsilvaSoft.SlopStudio.UnitTests/EsilvaSoft.SlopStudio.UnitTests.csproj --no-restore --filter FullyQualifiedName~LiteDbAgentAuthorizationPolicyTests -p:UsedAvaloniaProducts=`.

O teste de erro de escrita usa uma restrição de índice injetada; não homologa queda de energia, corrupção física do arquivo, disco cheio ou recuperação nativa em Linux. Restore oficial, suíte integrada e revisão independente pertencem ao gate da meta.
