# Release v0.5.0 — MVP

**Arquivada em 18/09/2026 por escopo funcional.** A homologação Windows/Linux **não** está concluída; as pendências estão em [`pendencias-de-homologacao.md`](pendencias-de-homologacao.md) e continuam abertas.

Esta pasta corresponde à [Fase 1](../../phases/phase-01-v0.5.0/README.md) do [roadmap](../../09-plano-de-implementacao.md).

## Objetivo entregue

Ciclo diário básico: conectar → navegar → consultar → visualizar → editar → exportar, sem exigir IA ou automação entre servidores.

## Requisitos concluídos e aceitos

| IDs | Recorte concluído | Evidência |
| --- | --- | --- |
| CON-01 | CRUD de perfis, pasta, tags, favoritos, cor e ambiente | `LiteDbConnectionProfileRepository`, `ConnectionsViewModel`; `ConnectionProfileDraftTests`, `LiteDbConnectionProfileRepositoryTests` |
| DAT-01, UX-01 | Navegação Connection → Database → Collection e detalhes básicos; abrir coleção prepara a consulta sem executar | `MainWindow.Explorer`, `WorkspaceViewModel`; `DatabaseExplorerTests`/`UiTests` |
| DAT-03 | `find`/`findOne`, filtro, `sort`, `limit`, `skip` | `ConsoleDatabaseSession`, `MongoQuery`; `ConsoleRuntimeTests`, `ConsoleMongoIntegrationTests` |
| DAT-04/05/06/08 | CRUD e edição protegida com releitura e conflito | `MongoWorkspaceService`, `DocumentMutationViewModel`; `DocumentUpdateRequestTests`, `ResultPanelTests` |
| DAT-09 | Contagem e distinct | `CountDocumentsAsync`/`GetDistinctValuesAsync`; `CollectionCountRequestTests`, `DistinctValuesRequestTests` |
| EDT-01/03 | JSON/árvore, BSON, UUID, ObjectId e datas | `StructuredResults`, `UuidCodec`, `IdentifierRepresentation`; `UuidCodecTests`, `IdentifierModeTests`, `BsonDatePresentationTests` |
| EDT-02 | Autocomplete determinístico simples | `CompletionService`, `TolerantParser`, `CompletionWindowPresenter` |
| EDT-04 | Histórico, consultas salvas e arquivos locais | `WorkspaceService`, `LocalScriptFileService`; `SavedQueryTests`, `LocalScriptFileServiceTests` |
| EDT-08 | Formatação de JSON, query e script com cancelamento e undo | `ExtendedJsonFormatter`; `ExtendedJsonPresentationTests` |
| TRF-01 | Exportação JSON e CSV da página, incremental, com progresso e cancelamento | `QueryResultExportSerializer`; `QueryResultExportSerializerTests` |
| ADM-11 | Auditoria local de metadados, sem dados sensíveis | `AuditJsonSerializer`; `AuditEntryTests`, `AuditJsonSerializerTests` |
| UX-01/02 | Abas, temas, cancelamento por aba e recuperação de rascunho | `WorkspaceTabViewModel`, `WorkspaceViewModel`; `WorkspaceUiTests`, `ConsoleWorkspaceTests` |

**CON-02/03/06/08/09** entraram no recorte básico de conexão, mas o requisito amplo (TLS/X.509, todas as topologias, inventário FCV/permissões) permanece aberto — ver [inventário](../../24-inventario-roadmap.md).

## Contrato CSV (TRF-01)

Exporta somente a página carregada, explicitamente indicada. Cabeçalhos são a união dos campos de primeiro nível em ordem de primeira ocorrência. Objetos, arrays e valores BSON tipados são serializados em Extended JSON canônico dentro da célula, sem flattening implícito. Campo ausente vira célula vazia; `null` vira o literal `null`. Aspas internas são duplicadas; delimitador, quebras de linha e aspas exigem célula entre aspas. UTF-8, separador vírgula, cabeçalho presente. Strings e cabeçalhos iniciados por `=`, `+`, `-`, `@`, tab, CR ou LF recebem apóstrofo inicial, por proteção de fórmulas. CSV é intercâmbio tabular, **sem** promessa de round-trip BSON.

## Documentos desta release

- [25 — Auditoria do MVP e polimento de performance](25-auditoria-mvp-performance.md)
- [Pendências de homologação](pendencias-de-homologacao.md)

## O que esta release não afirma

Não afirma homologação em Windows e Linux, publicação remota, cobertura do requisito amplo do catálogo, nem aceite de qualquer funcionalidade antecipada presente no checkout (agregação, administração, Console entre conexões, IA local).
