# Arquitetura

## Princípios

1. **UI sempre responsiva.** A UI thread só captura snapshot, cursor e destino (custo constante) e aplica resultados. Parsing, consulta, ranking e inferência rodam fora dela.
2. **Um entendimento do cursor por versão.** O Context Engine produz `CompletionContext` uma vez; tradicional, IA e preemptivo o reutilizam.
3. **Conhecimento como dados.** Linguagem MongoDB (métodos, operadores, stages, shapes) vive em dados versionados; adicionar comando não exige novo `if`.
4. **Nenhum I/O no caminho da tecla.** O catálogo responde de snapshots em memória; cargas remotas são assíncronas, deduplicadas e canceláveis.
5. **IA opcional e isolada.** O runtime ONNX permanece genérico; especialização MongoDB fica no Context Engine e no AI Context Builder.
6. **Somente a requisição atual atualiza a UI.**
7. **Metadado antes de dado.** Nomes e tipos são suficientes; valores de documentos não são armazenados nem enviados ao modelo.
8. **Reuso antes de criação.** Serviço de modelos, runtime, adapters, lexer, resolução de alvo e inferência de campos são evoluídos, não duplicados.

## Camadas e projetos

Não se cria novo projeto de domínio nesta meta ([AC-04](decisions.md)). Os componentes ficam em namespaces isolados, verificados por teste de arquitetura.

| Projeto / namespace | Componentes novos ou alterados |
| --- | --- |
| `Core` | `AutocompleteSettings` (campos aditivos), `EditorKeyBindings` (novo, aditivo em `WorkspacePreferences`) |
| `Application.Language.Text` | `ITextSnapshot`, `TextSpan`, `TextSnapshotVersion` |
| `Application.Language.Syntax` | `MongoLexer` (extraído do highlighting), `MongoSyntaxTree`, `TolerantParser`, `SyntaxTreeCache` |
| `Application.Language.Catalog` | `LanguageDefinition` (dados embutidos), `IKnowledgeCatalog`, `ICatalogSource`, `CatalogSymbol`, `NameIndex`, `IMetadataCache`, `CollectionSchema`, `SchemaBuilder`, `IMetadataInvalidationBus` |
| `Application.Language.Context` | `CompletionContextEngine`, `CompletionContext`, `ShapeWalker`, `NamespaceTargetResolver` |
| `Application.Language.Completion` | `CompletionService`, `CompletionItem`, `CompletionRanker`, `SnippetTemplate`, `CompletionUsageTracker`, `EditorRequestScope` |
| `Application.Language.Ai` | `RelevantContextSelector`, `AiContextBuilder`, serializadores de contrato, `AiCompletionProvider`, `CompletionOutputProcessor` |
| `Application.Language.Inline` | `InlineCompletionCoordinator`, fontes das camadas 0 e 1 |
| `Application` (existente) | `ILocalAiModelService`, `PriorityGate`, `ICompletionPromptBuilder`, `ITokenizer`, `CompletionPrivacy`, `IncrementalCompletion` |
| `Infrastructure` | `MongoMetadataSource` (driver), extensões de `OnnxLocalModelRuntime` (prompt pré-tokenizado, `TokenizerStream`, prefix cache), `IModelAdapter` com contratos de contexto |
| `Desktop` | `AvaloniaTextSnapshot`, `CompletionWindowPresenter`, `SnippetInserter`, `GhostTextElementGenerator`, `EditorCommandDispatcher` |
| `tests/UnitTests` | Fixtures `Language/Cases/*.case`, testes por componente |
| `tests/Benchmarks` (novo) | BenchmarkDotNet — projeto de ferramenta, não de domínio |

```mermaid
flowchart BT
  Core
  App["Application<br/>Language.* sem pacotes"] --> Core
  Infra["Infrastructure<br/>driver, ONNX, Jint"] --> App
  Desk["Desktop<br/>AvaloniaEdit, Avalonia"] --> Infra
  Bench["Benchmarks"] --> Infra
  Tests["UnitTests"] --> Desk
```

Regras verificadas por teste de arquitetura (reflexão sobre referências de assembly e namespaces):

- `Application.Language.*` não referencia Avalonia, AvaloniaEdit, MongoDB.Driver, Acornima nem ONNX.
- `Application.Language.Catalog` e `.Context` não referenciam `.Ai` nem `ILocalAiModelService`.
- Nenhum tipo `Microsoft.ML.*` fora de Infrastructure (regra atual mantida).

Extração futura para `EsilvaSoft.SlopStudio.Language`: somente quando os contratos da Fase 2 estiverem estáveis por pelo menos uma versão e houver necessidade real (ex.: reuso em ferramenta de linha de comando).

## Componentes

```mermaid
flowchart LR
  subgraph Desktop
    ED[MongoTextEditor]
    CMD[EditorCommandDispatcher]
    CWP[CompletionWindowPresenter]
    GH[GhostTextElementGenerator]
  end
  subgraph Application.Language
    SNAP[ITextSnapshot]
    LEX[MongoLexer]
    PAR[TolerantParser]
    CTXE[CompletionContextEngine]
    CAT[IKnowledgeCatalog]
    MC[IMetadataCache]
    CS[CompletionService]
    RK[CompletionRanker]
    SELc[RelevantContextSelector]
    ACB[AiContextBuilder]
    AIP[AiCompletionProvider]
    OUT[CompletionOutputProcessor]
    INL[InlineCompletionCoordinator]
  end
  subgraph Application
    LAMS[ILocalAiModelService]
    HL[SyntaxHighlightingService]
  end
  subgraph Infrastructure
    MMS[MongoMetadataSource]
    RT[OnnxLocalModelRuntime]
  end
  ED --> SNAP
  CMD --> CS
  CMD --> AIP
  ED --> INL
  SNAP --> LEX
  LEX --> HL
  LEX --> PAR --> CTXE
  CAT --> CTXE
  MC --> CAT
  MMS --> MC
  CTXE --> CS --> RK --> CWP
  CTXE --> SELc --> ACB --> AIP --> LAMS --> RT
  AIP --> OUT --> RK
  CTXE --> INL
  CS --> INL
  AIP --> INL
  INL --> GH
```

## Fluxos

### Tradicional (`Ctrl+.`)

Descrito no [README](README.md#fluxo-principal-ctrl). Pontos essenciais: snapshot em O(1) via `TextDocument.CreateSnapshot()`; contexto memoizado por versão e cursor; consulta ao catálogo sem I/O; lista marcada `IsIncomplete` quando algum escopo está carregando; `CatalogChanged` reconsulta a lista aberta se a versão ainda for a mesma.

### IA explícita (`Ctrl+;`)

```mermaid
sequenceDiagram
  participant V as Editor
  participant CE as Context Engine
  participant S as Relevant Context Selector
  participant B as AI Context Builder
  participant M as ILocalAiModelService
  participant O as Output Processor
  V->>CE: contexto (reutiliza versão)
  CE->>S: CompletionContext
  S->>B: fatos selecionados + orçamento
  B->>M: GenerateAsync(Interactive) com prompt do contrato do modelo
  Note over M: preempta geração Background; carga desacoplada
  M-->>O: tokens (streaming)
  O-->>V: prévia inline progressiva validada
  alt modelo indisponível, erro ou timeout
    M-->>V: exceção tipada
    V->>V: abre lista tradicional + motivo na barra de status
  end
```

### Preemptivo

Ver [preemptive-autocomplete.md](preemptive-autocomplete.md#fluxo). Camada 0 (determinística) responde sem debounce; camada 1 (IA) após debounce adaptativo, prioridade `Background`.

### Atualização de metadados

```mermaid
sequenceDiagram
  participant Q as Consulta ao catálogo
  participant MC as Metadata Cache
  participant SCH as Refresh Scheduler
  participant SRC as MongoMetadataSource
  participant BUS as Invalidation Bus
  Q->>MC: Get(escopo)
  alt fresco
    MC-->>Q: snapshot
  else stale ou ausente
    MC-->>Q: snapshot stale (ou vazio) + Loading
    MC->>SCH: agenda chave (single-flight, Low)
    SCH->>SRC: listCollections / listIndexes (token próprio)
    SRC-->>MC: novo snapshot imutável (troca atômica)
    MC--)Q: CatalogChanged(escopo)
  end
  BUS->>MC: invalida chave (DDL, Explorer, perfil, desconexão)
```

## Concorrência e cancelamento

### Escopo de requisição

Generaliza `CompletionSession`: um `EditorRequestScope` por editor **e por modalidade** (lista, IA explícita, inline). Cada requisição recebe versão monotônica e CTS próprio.

```text
Request A (versão 41, cursor 120)
  ↓ usuário digita "x"
Snapshot passa a versão 42 → Invalidate: versão++, A.Cancel()
Request B (versão 42, cursor 121)
  A termina depois de B? → versão 41 ≠ atual → descartada sem tocar a UI
  B termina → versão 42 = atual e cursor 121 = atual → aplicada
```

Regras:

1. A captura (snapshot, cursor, perfil, banco, coleção, modo) ocorre antes de qualquer `await`.
2. Toda aplicação na UI confere: editor anexado, `DataContext` inalterado, versão do documento, cursor, seleção vazia e destino da aba. Falha em qualquer item descarta silenciosamente.
3. Cancelamento é cooperativo; o descarte por versão protege contra providers que ignoram o token (comportamento já testado).
4. Uma modalidade não cancela a outra, exceto: `Ctrl+;` preempta a camada 1 do preemptivo (prioridade da fila); abrir a lista suspende o ghost.
5. Abas diferentes nunca compartilham CTS.

### Fontes de cancelamento

| Evento | Lista tradicional | IA explícita | Preemptivo |
| --- | --- | --- | --- |
| Digitação que altera o token do filtro | Reutiliza contexto e refiltra; não cancela | Cancela | Cancela, exceto typeahead sobre o ghost |
| Digitação fora do token / nova linha | Fecha e cancela | Cancela | Cancela e reagenda |
| Movimento do cursor | Fecha e cancela | Cancela | Cancela; não dispara sem edição |
| `Esc` | Fecha | Cancela geração | Descarta ghost |
| Troca de aba, destino, modo ou preferências | Cancela | Cancela | Cancela |
| Descarga/troca de modelo | — | Erro tipado → fallback | Camada 1 desativada |
| Timeout | — | Cancela e informa | Descarta silenciosamente |

### Recursos compartilhados

| Recurso | Controle |
| --- | --- |
| Modelo ONNX | `PriorityGate` existente: uma geração por vez; `Interactive` (chat, `Ctrl+;`, teste) antes de `Background` (inline); `Background` em execução é preemptado |
| Carga do modelo | Desacoplada do token do editor (existente): digitar não aborta a carga |
| Metadados | Single-flight por chave; token do scheduler, não do editor; cancelado só por invalidação, desconexão ou encerramento |
| Árvore sintática | Imutável por versão; reparse em worker; última versão vence |
| Catálogo | Snapshots imutáveis com troca atômica (`Volatile.Write`); leitores nunca bloqueiam |

## Degradação graciosa

| Situação | Tradicional | IA explícita | Preemptivo |
| --- | --- | --- | --- |
| Tudo disponível | Catálogo completo | Modelo | Camadas 0 e 1 |
| Metadados carregando | Linguagem + stale; lista incompleta | Fatos disponíveis | Camada 0 com o disponível |
| Conexão desconectada | Linguagem + cache existente; sem chamada remota | Idem | Idem |
| Permissão negada em metadado | Backoff; aviso discreto uma vez | Idem | Idem |
| Modelo ausente, inválido ou tokenizer incompatível | Inalterado | Lista tradicional + motivo | Só camada 0 |
| Provider GPU/NPU indisponível | Inalterado | Automático → CPU; explícito → mensagem | Camada 1 desativada se latência exceder orçamento |
| Erro de inferência / cooldown | Inalterado | Lista tradicional + motivo | Só camada 0 |
| Timeout | Inalterado | Mantém prévia parcial válida ou lista | Descarta |
| Contexto sensível detectado | Inalterado | Recusa com motivo | Só camada 0 |
| Parser não entende a posição | Linguagem genérica por papel lexical | Janela de texto sem fatos | Camada 0 lexical |

Nenhuma falha de IA ou metadado lança exceção até o handler da UI; o editor continua editável em todos os casos.

## Privacidade

- Catálogo armazena nomes, tipos, contagens relativas e origem da evidência; nunca valores de documentos.
- Nomes de chaves do cofre de ambientes podem ser sugeridos em `ENV.get("…")`; valores nunca.
- Prompt: somente nomes/tipos, trecho do editor e statements do histórico da mesma conexão; `CompletionPrivacy` aplicado por fato e no prompt final; marcadores reservados escapados.
- Métricas sem texto, nomes de campos, coleções ou queries.
- Histórico de aceite em memória; persistência só por opt-in e respeitando opt-outs existentes.

## Extensibilidade

| Extensão | O que muda |
| --- | --- |
| Novo método/operador/stage | Entrada nos dados da linguagem + caso de teste |
| Novo dialeto (ex.: mongosh completo) | Flag de dialeto, superfície nos dados, modo do lexer se necessário |
| Nova fonte de metadado | Implementar `ICatalogSource` declarando os tipos que serve |
| Nova família de modelo | `IModelAdapter` (existente) |
| Novo formato de contexto para modelo | Serializador de contrato + `contextContract` no metadata do modelo |
| Novo gatilho inline | Regra na política de gatilhos, com experimento registrado |

## Mapeamento de nomes da meta

| Meta | Projeto |
| --- | --- |
| `IAutocompleteModel` | `LocalModelDefinition` + `IModelAdapter` |
| `IModelTokenizer` | `ITokenizer` |
| `IInferenceRuntime` | `ILocalModelRuntime` |
| `IModelContextBuilder` | `AiContextBuilder` + serializadores de contrato + `ICompletionPromptBuilder` |
| Completion Provider | `CompletionService` + fontes do catálogo |
| AICompletionProvider | `AiCompletionProvider` |
| Output Processor | `CompletionOutputProcessor` |
| Knowledge Catalog | `IKnowledgeCatalog` |
