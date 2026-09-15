# Análise do repositório — situação atual

Inspeção estática do checkout `d23787e` (14/09/2026). Não houve build, execução de testes nem medição nesta etapa; afirmações sobre custo são **hipóteses a medir** na [Fase 1](phases/phase-1-data-traditional.md). Onde a documentação existente diverge do código lido, a divergência é registrada.

## 1. Solução e dependências

| Projeto | Referências | Pacotes relevantes | Papel no autocomplete |
| --- | --- | --- | --- |
| `Core` | — | — | DTOs persistidos e contratos simples: `AutocompleteSettings`, `AutocompleteRequest/Result`, `LocalModel*`, `MqlSuggestion`, `WorkspacePreferences` |
| `Application` | Core | nenhum (somente BCL) | Serviços de autocomplete, highlighting, serviço de modelos, prompt builders, fachada `WorkspaceService` |
| `Infrastructure` | Application | Jint 4.16 (Acornima transitivo), MongoDB.Driver 3.11.1, LiteDB 5.0.21, ONNX Runtime GenAI 0.15.2 (Cpu/WinML/Cuda) | Runtime ONNX, adapters, catálogo de modelos, driver MongoDB, Console Jint, validação/formatação com Acornima |
| `Desktop` | Infrastructure | Avalonia 12.1.2, AvaloniaEdit 12.0.0, CommunityToolkit.Mvvm 8.4 | Editor, views, view models, composition root |
| `UnitTests` | todos | NUnit 4.6, Avalonia.Headless | Testes unitários, Headless UI e integrações `Explicit` |

Regra vigente: Application não depende de pacotes; tipos ONNX, driver e Acornima ficam em Infrastructure; Desktop só conhece contratos. A nova arquitetura respeita essa regra (ver [architecture.md](architecture.md#camadas-e-projetos)).

## 2. Editor

- [`MongoTextEditor`](../../src/EsilvaSoft.SlopStudio.Desktop/SyntaxHighlighting/MongoTextEditor.cs) herda `AvaloniaEdit.TextEditor`, com linhas visuais virtualizadas, undo e seleção nativos. Expõe `Text`, `CaretIndex`, `SelectionStart/End` como propriedades Avalonia ligadas ao view model.
- A cada alteração, `OnTextChanged` copia `base.Text` (string do documento inteiro) para a propriedade ligada, que propaga para `WorkspaceTabViewModel.Text`. Consumidores de autocomplete trabalham sobre essa string completa, e não sobre `TextDocument.CreateSnapshot()`.
- Highlighting: [`SyntaxHighlightingService`](../../src/EsilvaSoft.SlopStudio.Application/SyntaxHighlighting/SyntaxHighlightingService.cs) é um lexer tolerante com estado entre linhas (aspas, comentários, pilha de frames `{`/`[`/`(`, pendências de `$search`/`aggregate`), cache por linha e alinhamento de sufixo. Roda com debounce de 80 ms em `Task.Run`. Classifica nomes de conexão/banco/coleção/índice a partir de `SyntaxContext`.
- AvaloniaEdit 12.0.0 contém (verificado no assembly): `CompletionWindow`, `CompletionList`, `ICompletionData`, `OverloadInsightWindow`, snippets (`Snippet`, `SnippetReplaceableTextElement`, `SnippetBoundElement`, `SnippetCaretElement`, `InsertionContext`, `SnippetInputHandler`), `ITextSource`/`CreateSnapshot`/`ITextSourceVersion`, `TextAnchor`, `VisualLineElementGenerator`, `InlineObjectElement`, `FormattedTextElement`, `IBackgroundRenderer`. **Nenhum desses recursos de completion/snippet é usado hoje.**

## 3. Fluxos atuais de autocomplete

### 3.1 Ghost text preditivo automático

[`WorkspaceTabView.Autocomplete.cs`](../../src/EsilvaSoft.SlopStudio.Desktop/WorkspaceTabView.Autocomplete.cs) reage a mudanças de `Text`, `CaretIndex`, `SelectionStart` e `SelectionEnd`:

```mermaid
sequenceDiagram
  participant E as MongoTextEditor
  participant V as WorkspaceTabView (UI)
  participant VM as WorkspaceTabViewModel (UI)
  participant S as CompletionSession
  participant A as AutocompleteService
  participant B as BasicAutocompleteProvider
  participant AI as AiAutocompleteProvider
  E->>V: PropertyChanged (texto, cursor ou seleção)
  V->>V: InvalidateCompletion (cancela e oculta)
  V->>VM: CaptureAutocompleteRequest(texto, cursor)
  VM->>VM: GetObservedCompletionFields → MongoCompletionTarget.Resolve (re-lex do prefixo) → InferFieldPaths (até 8 documentos)
  VM->>VM: KnownAutocompleteNames (percorre a árvore do Explorer), histórico, AutocompleteContextBuilder.Build (regex de privacidade por item)
  V->>S: RequestAsync
  S->>A: GetImmediateCompletion (síncrono)
  A->>B: regex de privacidade + palavras do prefixo/sufixo + keywords + operadores
  alt dicionário encontrou
    B-->>V: sugestão imediata
  else
    S->>S: Task.Delay(150 ms)
    S->>A: GetCompletionAsync (JSON + SHA-256 do request, cache 64/30 s)
    A->>AI: GenerateAsync (Background)
    AI-->>V: sugestão
  end
  V->>V: confere texto/cursor/destino; posiciona overlay (InlineCompletionTextBlock)
```

Aceitação por `Tab` (inteira ou incremental via [`IncrementalCompletion`](../../src/EsilvaSoft.SlopStudio.Application/IncrementalCompletion.cs)), descarte por `Esc`. O ghost é um `TextBlock` sobre um `Canvas` que redesenha o trecho da linha antes do cursor, a sugestão e o sufixo (até 32 KiB) com cores do snapshot de highlighting ([`InlineCompletionTextBlock`](../../src/EsilvaSoft.SlopStudio.Desktop/InlineCompletionTextBlock.cs)).

Observações:

- A captura ocorre também em **navegação por setas** e mudanças de seleção; parar o cursor por 150 ms dispara inferência em segundo plano mesmo sem edição.
- Todo o trabalho antes do primeiro `await` roda na UI thread: substring do prefixo, lexing completo do prefixo (até 65 536 caracteres) sem snapshot anterior, `JsonDocument.Parse` de até 8 documentos de até 64 KiB, varredura da árvore do Explorer, regex de privacidade em até ~260 itens, regex de palavras sobre prefixo+sufixo.
- [21 — Autocomplete local](../21-autocomplete-local.md) afirma que a extração de campos "é reutilizada enquanto o resultado não muda"; em `GetObservedCompletionFields` não há memoização visível. Confirmar por benchmark.

### 3.2 Menu `Ctrl+Espaço`

[`WorkspaceTabView.axaml.cs`](../../src/EsilvaSoft.SlopStudio.Desktop/WorkspaceTabView.axaml.cs) (`ShowSuggestions`) monta um `MenuFlyout`:

1. Uma sugestão IA/básica (`GetCompletionAsync` com prefixo e sufixo de até 32 KiB cada, sem o Context Builder).
2. `MqlAutocompleteService.GetSuggestions`/`GetAggregationSuggestions` (40 operadores fixos + campos observados). A inserção usa `ApplySuggestion(prefix)`, que **substitui o intervalo de 0 até o cursor** pelo prefixo reescrito.
3. No Console, `ConsoleAutocompleteService` (regex sobre `db`/`ConnectionPool`, métodos com argumentos fixos como `updateOne({_id: 1}, {$set: {}})`), executado em `Task.Run` com operação visível na barra.

Não há ranking (ordem de concatenação), filtro enquanto digita, tipo/ícone, snippet com placeholders nem resolve tardio. A validade é conferida por versão, texto, cursor e destino antes de inserir — esse cuidado deve ser preservado.

### 3.3 Chat

`LocalModelAiChatService` e `WorkspaceTabViewModel.AiChat` usam o mesmo `ILocalAiModelService` com prioridade `Interactive`. Fora do escopo, mas qualquer mudança no serviço de modelos não pode regredi-lo.

## 4. Interpretação de contexto — mecanismos existentes

| Mecanismo | Técnica | Usado por | Limitação |
| --- | --- | --- | --- |
| [`AggregationCompletionContext`](../../src/EsilvaSoft.SlopStudio.Application/AggregationCompletionContext.cs) | Scanner de caracteres com pilha | Sugestões de agregação | Só distingue chave de stage, referência `$campo` e stage atual |
| [`MongoCompletionTarget`](../../src/EsilvaSoft.SlopStudio.Application/MongoCompletionTarget.cs) | Varre tokens do highlighting | Campos observados por coleção | Resolve alvo explícito; bem testado; não entende posição dentro do filtro |
| [`ConsoleAutocompleteService`](../../src/EsilvaSoft.SlopStudio.Application/ConsoleAutocompleteService.cs) | Regex de caminho | `db.`, `ConnectionPool.` | Não entende argumentos nem objetos |
| `MqlAutocompleteService.GetCurrentPrefix` | Recuo por caracteres | Operadores/campos | Sem noção de chave vs valor |
| [`BasicAutocompleteProvider`](../../src/EsilvaSoft.SlopStudio.Application/BasicAutocompleteProvider.cs) | Regex de palavras | Ghost imediato | Menor palavra que começa com o prefixo, sem contexto |
| [`IncrementalCompletion`](../../src/EsilvaSoft.SlopStudio.Application/IncrementalCompletion.cs) | Scanner | Aceitação por Tab | Adequado ao propósito; manter |

Resultado: seis leituras independentes do texto, regras sem representação comum e nenhum conceito de "o que se espera nesta posição".

## 5. Vocabulário MongoDB duplicado

| Local | Conteúdo |
| --- | --- |
| [`MongoSyntaxVocabulary`](../../src/EsilvaSoft.SlopStudio.Application/SyntaxHighlighting/MongoSyntaxVocabulary.cs) | Conjuntos amplos para cor (funções, ~50 operadores, ~30 stages, Atlas Search, tipos EJSON, keywords, DSL) |
| `MqlAutocompleteService.Operators` | 40 itens com descrição pt-BR e categoria |
| `BasicAutocompleteProvider.Keywords` | 27 palavras |
| `ConsoleAutocompleteService.Methods` | 15 métodos com argumentos de exemplo |
| `AutocompleteContextBuilder.Commands` e listas por dialeto | Texto de prompt (contrato de treino) |
| `ConsoleRuntime.ReadMethods/WriteMethods` | Métodos aceitos pelo host |
| [`ConsoleBootstrap.js`](../../src/EsilvaSoft.SlopStudio.Infrastructure/ConsoleBootstrap.js) | **Superfície real** do Console: `db`, `getConnection`, `ConnectionPool`, `ENV`, `console`, `EJSON`, construtores UUID/ObjectId/NumberLong/NumberDecimal/Date/ISODate; métodos de coleção, cursor (`sort`, `skip`, `limit`, `project`, `hint`, `collation`, `comment`, `batchSize`, `maxTimeMS`, `toArray`) e banco (`getCollection`, `getSiblingDB`, `getName`, `dropDatabase`, `createCollection`, `stats`) |

Divergências exemplares: o highlighting reconhece `bulkWrite`, `findOneAndUpdate` e `getIndexes`, que o Console não expõe; `MqlAutocompleteService` não tem `$nor`, `$not`, `$type`, `$elemMatch`, `$size`, `$all`.

## 6. Metadados MongoDB

| Aspecto | Situação |
| --- | --- |
| Conexões | [`ConnectionProfile`](../../src/EsilvaSoft.SlopStudio.Core/ConnectionProfile.cs) (Id, Name, URI, `TargetHost` opcional); segredos em `IConnectionSecretStore`; sem campo de revisão — mudanças de configuração geram novo cliente em `MongoClientPool` por settings efetivos |
| Bancos | `IMongoWorkspaceService.GetDatabaseNamesAsync` |
| Coleções | `GetCollectionNamesAsync` → `ListCollectionNamesAsync` (sem tipo collection/view/timeseries) |
| Definição/validator | `GetCollectionDefinitionAsync` → `listCollections` filtrado por nome (uma chamada por coleção); inclui `options.validator` |
| Índices | `GetIndexesAsync` → `IExplorerMetadataService.ParseIndex` → [`IndexInfo`](../../src/EsilvaSoft.SlopStudio.Core/ExplorerMetadata.cs) com `Keys` em JSON |
| Amostra | Ferramenta de validador: `QueryAsync(limit 200, maxTimeMS 2000)` + `InferJsonSchema`/`InferFieldPaths` (documentos completos trafegam) |
| Campos no autocomplete | Somente resultados da aba, até 8 documentos, `InferFieldPaths` (reconhece wrappers EJSON, profundidade 12) |
| Cache | Apenas a árvore do Explorer ([`ExplorerNodeViewModel`](../../src/EsilvaSoft.SlopStudio.Desktop/ViewModels/ExplorerNodeViewModel.cs)): carga ao expandir, geração para descartar respostas antigas, `Invalidate()` recursivo, sem TTL |
| Exposição | `WorkspaceViewModel.Register` injeta `KnownSyntaxNamespaces` (achatamento da árvore, até 4096) e `KnownAutocompleteNames` (até 256) em cada aba |

Todas as chamadas remotas passam por `WorkspaceService.TrackAsync`, que publica operação na barra de atividades.

## 7. IA e ONNX

| Componente | Responsabilidade | Avaliação |
| --- | --- | --- |
| [`ILocalAiModelService`/`LocalAiModelService`](../../src/EsilvaSoft.SlopStudio.Application/LocalAiModelService.cs) | Dono único do modelo: descoberta, carga lazy desacoplada do editor, troca, fila `PriorityGate` (Interactive antes de Background, preempção), cooldown de 30 s, capacidades, teste | Reutilizar sem mudar responsabilidade |
| [`ILocalModelRuntime`/`OnnxLocalModelRuntime`](../../src/EsilvaSoft.SlopStudio.Infrastructure/OnnxLocalModelRuntime.cs) | Inicialização por plano de providers, geração greedy/amostrada, cancelamento por `terminate_session`, fallback GPU → CPU em Automático | Estender |
| [`IModelAdapter`](../../src/EsilvaSoft.SlopStudio.Infrastructure/ModelAdapters.cs) | Validação, tokenizer, prompt builder e paradas por família (Qwen nativo, DeepSeek .NET) | Reutilizar |
| [`LocalModelCatalog`](../../src/EsilvaSoft.SlopStudio.Infrastructure/LocalModelCatalog.cs) + `slopstudio-model.json` | Subpastas como modelos, validação estrutural, metadata opcional (capacidades, hardware, orçamentos) | Reutilizar; acrescentar campos |
| [`AiProviderSelector`](../../src/EsilvaSoft.SlopStudio.Infrastructure/AiProviderSelector.cs) + [`OnnxHardwareProbe`](../../src/EsilvaSoft.SlopStudio.Infrastructure/OnnxHardwareProbe.cs) | Plano NPU → GPU → CPU a partir de `GetEpDevices`; escolha explícita sem fallback | Reutilizar |
| `ICompletionPromptBuilder` (Qwen/DeepSeek) | Tokens FIM, orçamento 25% sufixo | Reutilizar; otimizar |
| `AiAutocompleteProvider` (dentro de `AutocompleteService.cs`) | Chama o serviço, limpa saída (`CleanGeneratedText`) | Refatorar |
| [`AutocompleteContextBuilder`](../../src/EsilvaSoft.SlopStudio.Application/AutocompleteContextBuilder.cs) | Cabeçalho `/* Local editor context … */` com listas | **Contrato de treino** dos pacotes SlopCoder ([23](../23-onnx-slopcoder.md)); congelar como v1 |

Observações de desempenho no runtime (a medir):

- Cada geração cria `GeneratorParams` e `Generator` e faz `AppendTokens` do prompt inteiro — prefill completo a cada pedido, sem reuso de KV cache. O assembly 0.15.2 expõe `Generator.RewindTo`/`TokenCount`, que permitem reuso de prefixo.
- `QwenFimPromptBuilder` codifica os três marcadores a cada chamada e o prefixo/sufixo completos antes de cortar por tokens; `RequireFullContext` codifica prefixo e sufixo novamente.
- Com sufixo ≥ 2 caracteres, cada token gerado decodifica toda a saída acumulada para detectar eco do sufixo (custo quadrático no número de tokens). `TokenizerStream` existe na API.
- `intra_op_num_threads = 0` usa todos os núcleos físicos; em inferência de fundo pode competir com a UI.
- `LocalAiModelService.TestModelAsync` usa `AutocompleteContextBuilder.ModelPrefix` — acoplamento leve entre serviço genérico e prompt MongoDB.

## 8. Caches existentes

| Cache | Escopo | Política |
| --- | --- | --- |
| `AutocompleteService._cache` | Respostas IA | 64 entradas, 30 s, chave SHA-256 do JSON do request + revisão das preferências; remoção da primeira chave |
| Highlighting | Por editor | Linhas por texto/estado, até 100 000 linhas |
| Explorer | Por conexão | Árvore carregada sob demanda, sem TTL |
| Sessão ONNX | Processo | Um modelo, lazy, reutilizado |
| `OnnxHardwareProbe` | Processo | `Lazy` |
| `MongoClientPool` | Processo | Até 64 configurações |

## 9. Concorrência e invariantes

- `CompletionSession`: uma por editor, versão monotônica + CTS; rejeita respostas tardias mesmo se o provider ignorar o cancelamento (testado).
- `PriorityGate`: um detentor, maior prioridade primeiro, FIFO por prioridade.
- Invariantes do [`AGENTS.md`](../../AGENTS.md) que a nova arquitetura mantém: contexto capturado antes de awaits; CTS nunca compartilhado entre abas; resultado só atualiza a aba de origem; Explorer nunca executa consulta automaticamente; nada de resultados/credenciais em snapshots.

## 10. Diagnóstico, operações e atalhos

- [`IAutocompleteDiagnostics`](../../src/EsilvaSoft.SlopStudio.Infrastructure/AutocompleteDiagnostics.cs): `Trace.WriteLine(evento, detalhe, duração)`; política de não registrar código, prompt ou exceção nativa bruta.
- `IApplicationOperationService`: operações visíveis com prioridade `Low/Normal/High`.
- Atalhos: `MainWindow.OnWorkspaceKeyDown` (Ctrl+T/W/O/S/Tab, F5, Ctrl+Enter, F6, Esc) e `WorkspaceTabView.EditorKeyDown` (Tab, Esc, Ctrl+Espaço), todos codificados. **Não existe sistema de keybindings.** `Ctrl+.` e `Ctrl+;` estão livres.

## 11. Testes existentes relacionados

`MqlAutocompleteServiceTests`, `MongoCompletionTargetTests`, `PredictiveAutocompleteTests`, `LocalAutocompleteTests`, `AutocompleteReliabilityTests`, `AutocompleteUiTests` (Headless, PNG nos dois temas), `LocalAiModelServiceTests`, `SyntaxHighlightingTests`/`SyntaxHighlightingUiTests`, `DeepSeekIntegrationTests` e testes `Explicit` com `SLOP_QWEN_MODEL`/`SLOP_DEEPSEEK_MODEL`. Não há projeto de benchmark nem conjunto-ouro de ranking.

## 12. Premissas da meta corrigidas

| Premissa | Correção |
| --- | --- |
| `db.Projetos.find({ Cliente.| })` e `{ Customer.Id: ... }` | Em JavaScript (Console, Script e Agregação usam sintaxe JS), chave com ponto sem aspas é erro de sintaxe. O Context Engine reconhece o padrão de forma tolerante e o item insere `"Cliente.Id"` com aspas, substituindo o trecho digitado. Ver [AC-09](decisions.md) |
| "Atalhos configuráveis caso exista sistema de keybindings" | Não existe. Proposto um registro mínimo de comandos persistido de forma aditiva, sem tela de edição nesta meta |
| Formato do contexto de IA livre para experimentação | Os pacotes SlopCoder foram treinados com o cabeçalho atual; mudar o formato exige contrato versionado por modelo ([AC-10](decisions.md)) |
| Estrutura de diretórios de modelos a criar | Já existe (subpastas + `genai_config.json` + tokenizer + `slopstudio-model.json`); só se acrescentam campos opcionais |
| Autocomplete pode consultar dados livremente | Autocomplete "nunca executa consultas" ([21](../21-autocomplete-local.md)). Comandos de metadados podem ser lazy para a conexão conectada; amostragem de documentos só por ação explícita ou opt-in ([AC-05](decisions.md)) |
| Abstrações `IAutocompleteModel`, `IModelTokenizer`, `IInferenceRuntime` a criar | Equivalentes já existem: `LocalModelDefinition` + `IModelAdapter`, `ITokenizer`, `ILocalModelRuntime`. Manter nomes do projeto |
| Tradicional só explícito | Mantido; abertura automática por `.`/`$` fica como opção desligada por padrão até haver métrica ([AC-17](decisions.md)) |

## Inventário: reutilizar, refatorar, substituir

| Componente | Decisão | Fase | Justificativa |
| --- | --- | --- | --- |
| `MongoTextEditor` | Manter; adaptar snapshot via `CreateSnapshot` | 2 | Base AvaloniaEdit adequada |
| `SyntaxHighlightingService` | Refatorar: extrair lexer compartilhado; highlighting consome os mesmos tokens | 2 | Evita segundo lexer; cache por linha já existe |
| `MongoSyntaxVocabulary` | Tornar projeção da linguagem embutida | 1 | Fonte única |
| `MqlAutocompleteService` (operadores, `GetSuggestions`, `GetAggregationSuggestions`, `ApplySuggestion`) | Substituir; manter como fachada obsoleta até remoção | 2 | Sem contexto estruturado; substituição 0..cursor |
| `MqlAutocompleteService.InferFieldPaths`/`InferJsonSchema` | Reutilizar dentro do construtor de schema; manter wrappers (validador usa) | 1 | Lógica testada, reconhece EJSON |
| `ConsoleAutocompleteService` | Substituir | 2 | Regex; pode chamar remoto |
| `AggregationCompletionContext` | Substituir | 2 | Coberto por parser + shapes |
| `MongoCompletionTarget` | Evoluir para resolvedor de alvo sobre a árvore; manter os casos de teste | 2 | Regras corretas (não herdar coleção de variável dinâmica) |
| `BasicAutocompleteProvider` | Refatorar como fonte da camada 0 usando catálogo e tokens | 5 | Evita regex sobre o documento |
| `CompletionPrivacy` | Reutilizar | 3 | Filtro conservador testado |
| `IncrementalCompletion` | Reutilizar | 5 | Aceitação incremental validada |
| `CompletionSession` | Generalizar como escopo de requisição por editor e modalidade | 2, 5 | Versão + CTS já corretos |
| `IAutocompleteService`/`AutocompleteService` | Manter como fachada de preferências, status e teste; caminho de sugestão migra | 4–5 | Compatibilidade com UI de preferências |
| `AiAutocompleteProvider` | Refatorar em provider + output processor | 4 | Separar orquestração, limpeza e validação |
| `AutocompleteContextBuilder` | Congelar como serializador `editor-context-v1` | 3 | Contrato de treino |
| `ILocalAiModelService`, `PriorityGate` | Reutilizar | — | Responsabilidade correta |
| `ILocalModelRuntime`/`OnnxLocalModelRuntime` | Estender: prompt pré-tokenizado, streaming, prefix cache experimental | 4 | Latência |
| `IModelAdapter`, `LocalModelCatalog`, metadata | Reutilizar; campo `contextContract` | 3 | Multimodelo já resolvido |
| `AiProviderSelector`, `OnnxHardwareProbe` | Reutilizar; política de latência por modalidade | 4 | Seleção já correta |
| `InlineCompletionTextBlock` + `CompletionPanel`/`GhostLayer` | Substituir por elemento visual do AvaloniaEdit (avaliar multilinha) | 5 | Layout nativo |
| `ShowSuggestions` (`MenuFlyout`) | Substituir por `CompletionWindow` | 2 | Filtro, navegação, snippets |
| `EditorKeyDown`/`OnWorkspaceKeyDown` | Refatorar para despacho por comandos | 2 | Atalhos como dados |
| `ExplorerNodeViewModel` | Manter como UI; escrever no Metadata Cache ao carregar | 1 | Evita carga dupla |
| `KnownSyntaxNamespaces`/`KnownAutocompleteNames` | Substituir por consulta ao catálogo | 1–2 | Remove varredura da árvore por tecla |
| `IMongoWorkspaceService`/`ExplorerMetadataService` | Reutilizar; acrescentar listagem de coleções com tipo e validator por banco | 1 | Uma chamada por banco |
| `WorkspaceService` | Publicar invalidação após DDL | 1 | Frescor |
| `IAutocompleteDiagnostics` | Manter; complementar com `Meter` | 1 | Métricas agregáveis |
| `MongoCodeValidator`/`MongoCodeFormatter` (Acornima) | Manter para validação/formatação; não usar para contexto do cursor | — | Acornima não tolera código incompleto |
| `ConsoleRuntime`/`ConsoleBootstrap.js` | Fonte de verdade da superfície do Console; teste de contrato com o catálogo | 1 | Evita divergência |

## Impactos da nova arquitetura

- **UI:** `AutocompleteUiTests` e evidências PNG precisam ser refeitos para `CompletionWindow` e ghost nativo nos 18 cenários de tema/tamanho/escala.
- **Preferências:** novos campos aditivos em `AutocompleteSettings` versão 1 e registro de atalhos em `WorkspacePreferences`; sessões antigas recebem padrões; sessão ilegível nunca é sobrescrita.
- **Memória:** novo cache de metadados com orçamento explícito e LRU.
- **Documentação e ADRs:** [06](../06-editor-bson-e-uuid.md), [17](../17-design-system-ui-ux.md), [21](../21-autocomplete-local.md), [22](../22-syntax-highlighting.md), [26](../26-ia-local-multimodelo.md); ADR-007, 023, 027, 030, 031 e 037.
- **Modelos:** pacotes SlopCoder continuam com contrato v1; formatos novos exigem avaliação e possivelmente novo treino.
- **Roadmap:** fases 1–2 materializam EDT-02 (v0.6.0); 3–5 pertencem à v0.9.0.
