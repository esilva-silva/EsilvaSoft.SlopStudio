# Análise do código para a v0.11.0

Leitura arquitetural em 22/09/2026 da solução, referências entre projetos, composição, contratos e caminhos de execução relevantes. Esta análise não é auditoria linha a linha de todos os arquivos nem homologação real de providers/MongoDB. Distingue fatos observados no código das extensões propostas; nenhum arquivo de produto foi implementado nesta meta.

## Inventário da solução inteira

[EsilvaSoft.SlopStudio.slnx](../../../EsilvaSoft.SlopStudio.slnx) contém sete projetos de produto e dois de testes/medição; todos seguem `Directory.Build.props`, `Directory.Packages.props` e locks por backend. `tools/BrandAssets` está fora da slnx. Projetos Atlas, IntegrationTests, UiTests e ArchitectureTests citados na arquitetura são planejamento, não projetos presentes.

| Projeto existente | Evidência principal / responsabilidade | Reuso e impacto da v0.11.0 |
| --- | --- | --- |
| `Core` | csproj sem pacotes; `ConnectionProfile`, `MongoQuery`, `AggregationQuery`, requests de mutação/admin, `IdentifierRepresentationService`, DTOs de workspace | Novos records/enums em Agents; zero SDK/driver/transportes. `MongoQuery` limita 1–1.000 docs, skip até 1.000.000, maxTime até 600.000 ms; integração externa deve restringir mais |
| `Autocomplete.Core` | Referencia Core; Language/Text/Syntax/Completion, catálogo de linguagem embarcado, contratos `IMongoMetadataSource`, schemas e contexto | Preservar parsing/completion determinísticos offline. Não transformar contexto de autocomplete em autorização para enviar schema ao provider |
| `LocalAi.Core` | Referencia Core, zero NuGet; `ILocalAiModelService`, `ILocalModelRuntime`, tokens/modelos/capabilities | Adaptar geração ao runtime sem renomear o núcleo como IA genérica e sem incluir segredos externos |
| `Application` | Referencia três núcleos; `WorkspaceService`, `MetadataCache`, `SchemaLearning`, `AiContext`, `LocalAiModelService`, `LocalModelAiChatService` | Sede de runtime/registry/políticas e adaptação local. Portas já separam I/O; evitar fachada paralela de Mongo |
| `Infrastructure` | Referencia Application; MongoDB.Driver, LiteDB, Jint; `MongoWorkspaceService`, executores especializados, Console/mongosh e updates | Reutilizar serviços e proprietário local. Novos parsing literal, cofre de SO, persistência e broker IPC; sem SDK de provider neste assembly |
| `Infrastructure.LocalAi` | Referencia Application/LocalAi.Core; pacotes ONNX selecionados por backend Cpu/WinML/Cuda | Preservar isolamento de banco/UI, tokenizer e modelo único; sem HTTP para inferência externa. Downloads explícitos de modelos continuam separados |
| `Desktop` | Avalonia/MVVM; referências Application + duas infraestruturas; `App.axaml.cs`, Workspace/abas/assistente/preferences | Novo painel nativo consome runtime e capabilities; nenhum driver ou protocolo Codex/Claude em ViewModel |
| `UnitTests` | NUnit/Avalonia.Headless e fixtures: BSON/UUID, MongoQuery, contextos, sessões, persistência, renderização e IA | Estender contratos/políticas/IPC e cenários de falha; headless não prova conta real, diálogo nativo ou leitor de tela |
| `Benchmarks` | BenchmarkDotNet e NUnit; mede linguagem, aprendizagem e runtime ONNX; não publicável | Medir filas, serialização BSON e pressão de streaming quando implementados; benchmark/modelo real explícito não vira requisito com segredo em CI |

O grafo real não inclui MongoDB.Bson em Core, apesar de um trecho histórico em `docs/05` admitir essa possibilidade. A regra vigente e compilada é Core sem pacotes (ADR-040). `OperationContext`, `IQueryService`, `ISecretStore` e outras portas antigas da arquitetura são conceitos propostos; não confundir com implementações atuais. Existem `MongoOperationContext` e `OperationEnvironment` internos em Infrastructure.

## Caminhos existentes inspecionados

### MongoDB e proteção de execução

[WorkspaceService](../../../src/EsilvaSoft.SlopStudio.Application/WorkspaceService.cs) coordena os casos de uso, barra de operações, histórico e invalidação de metadados. [IMongoWorkspaceService](../../../src/EsilvaSoft.SlopStudio.Application/IMongoWorkspaceService.cs) oferece listagem, queries, count/distinct/explain, aggregate, índices, CRUD, administração e import/export. [MongoWorkspaceService](../../../src/EsilvaSoft.SlopStudio.Infrastructure/MongoWorkspaceService.cs) prepara contexto por chamada e delega para `MongoQueryExecutor`, `MongoDocumentMutator`, `MongoIndexManager`, `MongoDatabaseAdministrator`, `MongoServerAdministrator` e `MongoDatabaseExportImportService`.

`MongoOperationContext.PrepareAsync` cria `OperationEnvironment`, captura ambiente/cofre e resolve URI, usando `MongoClientPool`. `ConnectionProfile.EnsureWriteAllowed()` protege métodos de escrita da fachada; filtros não vazios e requests fazem validações adicionais. Essas proteções são reaproveitáveis, mas não representam política por agente ou aprovação humana central: UI/Console possuem suas confirmações específicas. Atualização de documento usa `UpdateOneAsync`; wrapper externo deverá acrescentar precondição atômica antes de prometer edição concorrente segura.

[MongoQueryExecutor](../../../src/EsilvaSoft.SlopStudio.Infrastructure/MongoQueryExecutor.cs) usa cursor e canonical Extended JSON; find/aggregate limitam página a 8 milhões de caracteres. Count retorna Int64 e distinct usa pipeline `$group` limitado. Explain find utiliza `executionStats`; explain aggregate utiliza `queryPlanner`. Aggregate usa MaxTime de 5 minutos fixo, sem campo de deadline em `AggregationQuery`; isso é gap para exposição externa. [AggregationPipelineValidator](../../../src/EsilvaSoft.SlopStudio.Infrastructure/AggregationPipelineValidator.cs) bloqueia `$out`/`$merge` estruturalmente, inclusive pipelines de `$facet`, `$lookup` e `$unionWith`, mas não autoriza namespaces cruzados por principal.

O parser atual resolve UUID constructors e valores dinâmicos `ENV`. MCP não pode expor esse poder: uma consulta aparentemente de leitura poderia introduzir segredo do ambiente em expressão/saída. Separar parsing literal de resolução da conexão é gate obrigatório. Evitar tool genérica `run_command`/`execute_script`, que contornaria a classificação por operação.

### Metadados, schema e aprendizagem

[MongoMetadataSource](../../../src/EsilvaSoft.SlopStudio.Infrastructure/MongoMetadataSource.cs) fornece metadados e `SampleSchemaAsync` com limite/MaxTime; `ListDatabaseNamesAsync` e `ListCollectionNamesAsync` já pedem `AuthorizedDatabases=true`/`AuthorizedCollections=true`. Os métodos públicos atuais de `MongoWorkspaceService` para esses nomes não pedem essas opções, portanto tools devem preferir a porta `IMongoMetadataSource` e ainda aplicar grants Slop antes de transmitir metadados. A amostra projeta tipos/campos. `MetadataCache` coordena atualização/invalidação e `SchemaBuilder` compõe conhecimento. `SchemaLearningService`/coordenador/analisador mantêm conhecimento aprendido com policy, confiança, disponibilidade e opt-out; `LearnedSchemaCatalogSource` não é schema autoritativo.

`MongoOperationContext.ParseDocument` transforma construtores de UUID e resolve `ENV` via `DynamicValues.ResolveJson`; é caminho interno legado do Console e queries atuais. Não o usar para entradas externas de Agent/MCP. Implementar parser EJSON literal separado antes de habilitar `mongo_find`, `mongo_count` ou qualquer tool com filtro/projeção; erros não devem ecoar os argumentos. Saídas de `mongo_find` exigem limite em bytes com documentos BSON/EJSON íntegros. `mongo_count` retorna Int64 e precisa de Extended JSON/string para preservar precisão. `GetIndexesAsync` devolve documentos BSON de índices completos; projeção allowlist deve preceder qualquer exposição.

O registry deverá ter caso de uso explícito de schema que utiliza essa base, com categoria de dados e amostragem autorizada. Abrir conexão/provider, descobrir tools ou selecionar coleção não dispara query/amostragem. Nova escrita deve passar pelo caminho que invalida caches, especialmente rename/drop, em vez de chamar executores internos diretamente.

### Persistência, segredos e auditoria

[ServiceCollectionExtensions](../../../src/EsilvaSoft.SlopStudio.Infrastructure/ServiceCollectionExtensions.cs) registra uma instância `LiteDbConnectionProfileRepository` e resolve interfaces de perfis, histórico, queries salvas, auditoria, sessão, ambiente e aprendizagem para o mesmo objeto. [Proprietário LiteDB](../../../src/EsilvaSoft.SlopStudio.Infrastructure/LiteDbConnectionProfileRepository.cs) usa `Connection=direct`, `_gate` e `Task.Run` para facetas assíncronas; fila dedicada limitada é proposta histórica, não infraestrutura já comprovada nesse código.

[SessionConnectionSecretStore](../../../src/EsilvaSoft.SlopStudio.Infrastructure/SessionConnectionSecretStore.cs) é dicionário concorrente em memória; não é Windows Credential Manager nem Secret Service. Perfil pode conter URI e ambiente tem valores persistidos: não tratar o nome “vault” como prova de criptografia. A nova integração exige referências de segredos em storage de SO e redução explícita de DTOs. O pedido autoriza providers externos, mas não transforma IA local em serviço remoto.

`AuditEntry`/`IAuditRepository` continuam atendendo a auditoria local manual. A nova coleção `agentAuditEvents` adiciona principal, chamada, canal, tool, revisão, resultado e métricas sem conteúdo; está no owner LiteDB único e no DI, mas ainda não é consumida pelo registry. O schema v1 ainda não inclui namespace autorizado, motivo detalhado, approval ID/horário nem início/fim separados conforme o contrato de segurança; completar isso com migração versionada antes de afirmar gate de escrita. A revisão encontrou também correlação fraca de intents/desfechos que pode retirar indevidamente uma intenção pendente da retenção; correção pendente. Falha ao registrar intenção de escrita deve bloquear despacho; falha após despacho precisa preservar efeito MongoDB possível. Não abrir segundo workspace no processo MCP.

### Console, processos e arquivos

`IConsoleRuntime`/`ConsoleRuntime` usam Jint e proxies de `IConsoleDatabaseSession`, com confirmação de escrita e contexto. `MongoshScriptExecutionService` executa processo externo e trata saída por `MongoshOutputParser`; são runners existentes, não sandboxes genéricos para LLM. `LocalScriptFileService`, `LocalWorkspaceFileService` e `LocalFileMutationGate` protegem fluxos de arquivo da IDE. Não expor funções arbitrárias de arquivos, ENV, processos e console ao agente como conveniência.

Patterns de cancelamento/processos podem informar adapters, mas códigos/limites do mongosh permanecem preservados. Novo provider deve ter processo/canal e shutdown próprios; saída STDIO MCP não pode compartilhar logs com protocolo.

### IA e chat atuais

[LocalModelAiChatService](../../../src/EsilvaSoft.SlopStudio.Application/LocalModelAiChatService.cs) é o serviço registrado como `IAiChatService`: monta proposta de substituição do editor, verifica contexto/sensibilidade, pede geração ao modelo local e constrói diff. [AiChatService](../../../src/EsilvaSoft.SlopStudio.Application/AiChatService.cs) é fallback determinístico conservador, não LLM externo. Portanto “há chat” não comprova sessões/tools/approvals de um agente geral.

`AiGenerationPipeline`, `AiContextPipeline` e `ILocalAiModelService.StreamAsync` já tratam budget, streaming, prévias, carga e preempção; o modelo é proprietário compartilhado, prioritizado. Runtime novo deve aproveitar geração e políticas locais sem copiar protocolo FIM como protocolo de agentes. `CancelGeneration()` global não pode virar cancelamento de qualquer aba: usar token por solicitação e manter prioridades. Capabilities locais dependem do modelo e não incluem tools por suposição.

### Desktop e sessão

Workspace/WorkspaceTab ViewModels mantêm edição/contexto por aba; explorer navega. Assistente, diff e janelas de preferências fornecem padrões visuais, sem substituir a leitura do [design system](../../17-design-system-ui-ux.md). Novo chat usa snapshots de aba/documento, dispatcher assíncrono e eventos correlacionados, sem importar `ConnectionProfile` para mensagens remotas. Prefs e rascunhos continuam sujeitos a opt-outs e falhas de persistência visíveis; conversas não entram automaticamente no snapshot de workspace.

## Gaps e gates ordenados por risco

| Gap observado | Consequência | Gate antes da entrega |
| --- | --- | --- |
| Registry interno parcial; ledger v1 de chamada existe mas não está ligado ao registry e falha em correlacionar intents com segurança; sem broker/identidade MCP ou integração chat | Ingress externo ainda não pode invocar tools com identidade confiável ou auditoria equivalente | Corrigir correlação/evoluir schema, ligar auditoria fail-closed ao registry, completar identidade confiável e provar equivalência chat/MCP antes de registrar ingressos |
| Parsing dinâmico de entradas | ENV/segredos podem sair por consulta | Modo literal separado, fixtures de strings e nenhuma resolução dinâmica externa |
| Profiles/diagnósticos têm dados privados | Serialização automática vaza URI/host/ambiente | DTOs allowlist, cofre de SO, redator e testes de canários |
| Sem ledger de aprovações | Replay, consentimento obsoleto, auditoria insuficiente | Hash alvo/args/revisão/principal, expiração, one-shot e status de certeza |
| Limites existentes focam IDE | Consulta cara e saída excessiva via agente | Limites menores de bytes/tempo/concurrency e MaxTime no servidor |
| Aggregate atravessa coleções | Grant em A pode ler B | Autorizar todos os namespaces ou rejeitar operador |
| Mutação simples sem precondição atômica externa | Aprovação pode alterar documento diferente do revisado | Versão/predicado atômico, conflito explícito, sem retry automático |
| Sem IPC proprietário MCP | Segunda abertura LiteDB ou privilégios implícitos | Proxy sem banco, broker autenticado, recovery e testes Windows/Linux |
| Chat local não é agente geral | Promessa incorreta de capabilities | Adapter honesto, testes de contrato e preservação offline |
| Providers/SDKs não integrados | Instabilidade e autenticação incompatível | Spike oficial versionado, licença e credenciais reais fora da CI |

## Regras dos agentes de desenvolvimento

Revisão orientada por architecture, MongoDB domain, ONNX/local AI e persistence/security; catálogo [agents/README](../../../agents/README.md). Os agentes em `/agents` são instruções de desenvolvimento, não runtime do produto nem personas a copiar para prompts de usuário. A restrição de inferência local offline continua aplicável ao subsistema ONNX; autorização explícita desta meta cria adapters externos separados. Não remover essa proteção do módulo local para acomodar providers.

Este inventário cobre todos os projetos por responsabilidades e seus caminhos relevantes para integração. Evidência de testes existentes indica pontos de extensão, não execução nesta revisão: `MongoQueryTests`, `AggregationPipelineValidatorTests`, `MongoWorkspaceMutationValidationTests`, `UuidCodecTests`, `SessionConnectionSecretStoreTests`, testes LiteDB/sessão, `LocalAiModelServiceStreamingTests`, `AiGenerationPipelineTests` e renderização de workspace/assistente. A suíte a executar e a homologação real necessária estão no [plano de testes](11-plano-de-testes.md); lacunas não são encerradas por atualização documental.
