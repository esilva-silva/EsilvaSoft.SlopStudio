# Fase 1 — Dados tradicionais e aprendizado dinâmico

**Revisada em 18/09/2026: K11–K16 e K16-b concluídos, aceite ainda parcial.** Roadmap v0.6.0 (EDT-02); habilita Fase 2/3. Não reconstruir catálogo/benchmarks já existentes.

## Estado da implementação

LanguageDefinition/mongodb-language.v1.json, KnowledgeCatalog/NameTable, MetadataCache/MongoMetadataSource, SchemaBuilder, write-through/invalidations e AutocompleteMetrics/Benchmarks já existem no b082d4a. Campos de resultados são memoizados, mas aprendizado persistente ainda não existe. Detalhes em [current-state](../current-state.md).

### K11–K16 e K16-b — concluídos em 18/09/2026

Trabalho real entregue nesta rodada, sem reescrever o que já existia:

- **K11 (escopo reduzido).** A mudança de contrato de `ConnectionIdentity` proposta originalmente foi **rejeitada**; o requisito passou a cobrir apenas travas de regressão sobre o comportamento já existente: cache esvaziado após `InvalidateEnvironment` e hosts distintos com o mesmo nome de namespace sem reuso cruzado entre conexões. Gerações por chave, single-flight, write-through com guarda de geração, `SampleSchemaAsync` protegido contra desconexão/invalidação, `Peek` propagado e `Changed` terminal por chave **já existiam com teste antes desta meta** — não são entrega deste lote, só ficaram cobertos pelas duas travas novas.
- **K12.** Cancelamento adicionado ao scan de substring de `NameTable.Collect`, que antes varria a tabela inteira sem interrupção quando prefixo/camel humps não preenchiam o máximo pedido.
- **K13.** Limite de cargas simultâneas no `MetadataCache`: no máximo 2 por conexão e 4 globais, evitando que uma aba com muitas coleções recém-abertas sature o pool de conexão.
- **K14.** A memoização de mescla em `MetadataCatalogSource` deixou de ser um cache sem limite e virou LRU de 8 entradas.
- **K15.** `CollectionSchema.Merge` deixou de aceitar `int.MaxValue` como teto implícito de profundidade/nós e passou a respeitar `SchemaMaximumDepth`/`SchemaMaximumNodes`.
- **K16-b.** Cota por fonte em `KnowledgeCatalog.Query`: impede que uma fonte oculte totalmente outra quando ambas produzem candidatos do mesmo `kind`, mesmo com `MaximumCandidates` atingido. É pré-requisito de L15 (ainda não iniciado).

### Ainda aberto na Fase 1

- **K17** (relatório de performance do catálogo) não iniciado.
- **Toda a fase L (L11–L16, schema learning persistido em LiteDB) não iniciada.** Nada neste lote grava schema aprendido em disco.

### Desvios em relação ao plano

- **Validator por coleção, não por banco.** A primeira implementação carregava todos os validators do banco em uma chamada e retinha 99,7 MB no cenário 1 000 × 1 000. Tipo e validator passaram a ser carregados por coleção (`listCollections` filtrado pelo nome) dentro do LRU: 29,9 MB. Nomes continuam vindo de `nameOnly` + `authorizedCollections`.
- Arquivo de linguagem em `Application/Language/mongodb-language.v1.json`, sem subpastas `Catalog/Data`.
- `SymbolFlags` e `FieldFlags` foram renomeados para `SymbolTraits` e `FieldTraits` (regra CA1711 do repositório).
- O evento `ExplorerNodeViewModel.MetadataChanged` foi removido: as abas atualizam o contexto de highlighting pelo `IMetadataCache.Changed`.
- O opt-in de amostragem é persistido (`WorkspacePreferences.SchemaSamplingProfileIds`) e exposto em `WorkspaceViewModel.SetSchemaSamplingAllowedAsync`, sem controle visual; a UI pertence à Fase 2.
- A ferramenta de validador continua usando `QueryAsync` com documentos completos (item opcional desta fase, não alterado).
- `IMongoWorkspaceService` não mudou; a fonte de metadados é um serviço próprio da Infrastructure.

### Critérios de aceite

| # | Estado | Evidência |
| --- | --- | --- |
| 1 | 🚧 | Baseline dos componentes registrada em [performance](../performance.md#baseline-medida--fase-1); tempo de UI por tecla em Headless/nativo ainda não medido (instrumento `ui.autocomplete.dispatcher_time` disponível) |
| 2 | ✅ | `LanguageDefinitionTests`: integridade, cobertura do vocabulário e das sugestões atuais, contrato com as globais e proxies do Console |
| 3 | ✅ | `SyntaxHighlightingTests` e `SyntaxHighlightingUiTests` sem alteração, suíte aprovada |
| 4 | ✅ | `KnowledgeCatalogTests.MetadataIsAnsweredFromMemoryAndReportsUnavailableScopes`, `MetadataCacheTests.ConcurrentReadsShareOneLoadAndNeverBlock` |
| 5 | ✅ | 50 leituras concorrentes, 1 chamada remota |
| 6 | ✅ | `DisconnectedProfilesAndPeekNeverLoad` |
| 7 | ✅ | `WorkspaceOperationsPublishInvalidationsOnlyAfterSuccess`, `ConsoleWritesPublishInvalidationsForTheirNamespace`, `InvalidationsRemoveOrMarkExactlyTheAffectedKeys` |
| 8 | ✅ | `ExplorerLoadsWriteThroughAndFeedNamesWithoutRemoteMetadataCalls` |
| 9 | ✅ | `ResultsKeepDiscoveryOrderAndTypesButNeverValues`, pipeline de amostragem só com nomes e tipos; servidor real pendente de fixture |
| 10 | ✅ | `SchemaSamplingRequiresOptInOrAnExplicitAction` |
| 11 | ✅ | `CatalogQueryBenchmarks`: pior consulta com 10 000 campos em 0,15 ms de média, abaixo de 1 ms ([baseline](../performance.md#catálogo)); job curto, sem p95 |
| 12 | ✅ | `MemoryScenario`: 29,9 MB retidos, LRU de 64 entradas por conexão |
| 13 | 🚧 | Restore travado da solução e lockfiles das três variantes do projeto novo; build e testes executados só na variante padrão WinML |
| 14 | 🚧 | 15, 21, 24 e documentos desta pasta atualizados; ADRs ainda não promovidas para [10](../../10-decisoes-arquiteturais.md) |

Pendências: integração real de `MongoMetadataSource` (`MetadataSourceListsKindsValidatorsIndexesAndSamplesWithoutValues`, ignorado sem MongoDB portátil), medição de UI por tecla, segunda máquina de referência, builds Cpu/Cuda e Linux.


## Escopo revisado

Consolidar geração de conexão/chave, write-through/amostra tardia, Peek propagado, fila limitada, preservação de kinds/permissão, cache multiaba de mescla com limites e completude distinta de cobertura. Acrescentar Schema Discovery/Learning contínuo de find, deltas probabilísticos e LiteDB no proprietário único.

## Tarefas implementáveis

[K11–K17 e L11–L16](../execution-plan.md) especificam agente, arquivos, dependências, resultado, teste e aceite. MongoDB Knowledge é dono; Performance mede e Testing revisa desde a entrada. G00 fixa contratos antes de consumidores.

## Critérios de aceite da revisão

- Base existente reutilizada; nenhuma query por tecla/refiltro.
- Corridas de carga/write-through/amostra/opt-out reproduzidas e protegidas por geração.
- Limites globais de fila/bytes/mescla e tipos de coleção demonstrados.
- Resultado find entregue sem aguardar analyzer/LiteDB; zero consulta adicional; aprendido sem valores, com identidade e frequências corretas.
- Reinício recupera aprendido; delta/retry idempotente; erro/corrupção não sobrescreve cache ilegível nem sessão.
- Catálogo serve prefixo em memória; learned update altera só revisão/escopo necessário.
- Evidência histórica abaixo não conta como execução dos novos requisitos. Fonte real, perfis de hardware e gates pendentes permanecem declarados.

## Fora do escopo

Parser e UI pertencem à Fase 2; amostragem Mongo extra continua só explícita/opt-in. Não persistir documentos, credenciais ou resultados em workspaceSession. [Schema Learning](../schema-learning.md).
