# Análise do repositório — situação atual

Revisão estática de **15/09/2026**, checkout **`b082d4a`**. Substitui a análise de `d23787e`, anterior à Fase 1 e às consultas avançadas. Medições de 14/09 são históricas, não foram repetidas nesta revisão. Não se iniciou refatoração do produto.

## 1. Solução e dependências

Core → contratos; Application → Core/BCL; Infrastructure → Application; Desktop → Infrastructure. [Versões fixadas](../../Directory.Packages.props): .NET 10, Avalonia 12.1.2, AvaloniaEdit 12.0.0, MongoDB.Driver 3.11.1, Jint 4.16.0, LiteDB 5.0.21, GenAI 0.15.2, ORT Managed 1.28.0 e BenchmarkDotNet 0.15.8.

[ServiceCollectionExtensions](../../src/EsilvaSoft.SlopStudio.Infrastructure/ServiceCollectionExtensions.cs) já registra catálogo, fonte, cache e barramento como singletons, um proprietário LiteDB para os repositórios e um serviço de modelos compartilhado com chat. Namespace real da Fase 1: `Application.Language`. Subnamespaces dos esboços são organização futura, não tipos existentes.

## 2. Editor

[MongoTextEditor](../../src/EsilvaSoft.SlopStudio.Desktop/SyntaxHighlighting/MongoTextEditor.cs) deriva de AvaloniaEdit. `OnTextChanged` ainda materializa `base.Text` para o binding; highlighting roda em worker, com debounce e cache por linha. Migrar completion para `CreateSnapshot()` não elimina sozinho a cópia utilizada por execução e autosave.

[WorkspaceTabView.Autocomplete](../../src/EsilvaSoft.SlopStudio.Desktop/WorkspaceTabView.Autocomplete.cs) reage a texto, cursor e seleção, captura contexto na UI e aguarda `CompletionSession`. O ghost usa [InlineCompletionTextBlock](../../src/EsilvaSoft.SlopStudio.Desktop/InlineCompletionTextBlock.cs) sobre Canvas, redesenhando o sufixo. Há Tab incremental, Esc e descarte de respostas obsoletas. Presenter nativo compartilhado e snippets ainda são propostas.

## 3. Fluxos atuais de autocomplete

- [CompletionSession](../../src/EsilvaSoft.SlopStudio.Application/CompletionSession.cs): uma por editor, versão monotônica/CTS, dicionário síncrono antes do debounce, depois IA. Movimento sem edição também pode disparar.
- [BasicAutocompleteProvider](../../src/EsilvaSoft.SlopStudio.Application/BasicAutocompleteProvider.cs): palavras por regex, keywords e sugestões MQL; menor continuação ordinal. **Já é preemptivo determinístico lexical**, mas não contextual, sem ranker/confiança compartilhados.
- [AutocompleteService](../../src/EsilvaSoft.SlopStudio.Application/AutocompleteService.cs): cache IA 64 entradas/30 s; JSON + SHA-256 do request/revisão; `UseDictionary` independente de Mode. IA usa prioridade Background inclusive quando chamada pelo menu legado.
- [ShowSuggestions](../../src/EsilvaSoft.SlopStudio.Desktop/WorkspaceTabView.axaml.cs): Ctrl+Espaço, MenuFlyout, aguarda básico/IA e concatena MQL/Console. Sem filtro incremental, ranking ou placeholders; `ApplySuggestion` reescreve o prefixo até o cursor.
- Chat compartilha o serviço de modelos, prioridade Interactive; preservar seu ciclo de vida e testes.

## 4. Interpretação de contexto

| Código | Reutilização / lacuna |
| --- | --- |
| `SyntaxHighlightingService` | Lexer tolerante com estado/cache por linha; extrair preservando classificação |
| `MongoCompletionTarget` | Caminhos estáticos pelo lexer; preservar fixtures e tratar limite de 65 536 caracteres |
| `AggregationCompletionContext` | Scanner de posição stage/referência; migrar casos ao contexto comum |
| [AggregationFieldInference](../../src/EsilvaSoft.SlopStudio.Application/AggregationFieldInference.cs) | Já trata project/group/set/unset/lookup/facet/count; re-lex por chamada, 8 192 tokens/512 campos; transformação desconhecida limpa campos |
| `ConsoleAutocompleteService` | Regex de receptor/métodos/namespaces, não parser de filtros |
| MQL e básico | Prefixos/palavras, ainda regras duplicadas |
| `MongoCodeValidator`, `MongoCodeFormatter`, `ConsoleRuntime` | Acornima/Jint para validar/formatar/executar; não substituir por parser tolerante |

Não há AST de completion, `CompletionContextEngine` ou `ShapeWalker` compartilhados. A proposta não pode perder facet/count já existentes ou sugerir campos anteriores como certos após transformação desconhecida.

## 5. Vocabulário MongoDB duplicado

[LanguageDefinition](../../src/EsilvaSoft.SlopStudio.Application/Language/LanguageDefinition.cs) carrega [mongodb-language.v1.json](../../src/EsilvaSoft.SlopStudio.Application/Language/mongodb-language.v1.json); `MongoSyntaxVocabulary` já projeta os dados. Operadores MQL, keywords básicas, métodos Console e comandos do cabeçalho IA ainda têm listas próprias. Preservar o contrato de treino v1 ao consolidar as demais.

`LanguageDefinitionTests` compara a superfície Console com o bootstrap. Shapes, snippets, tipos, Since e flags Search presentes no catálogo não significam providers integrados. Console permanece limitado por [ConsoleBootstrap.js](../../src/EsilvaSoft.SlopStudio.Infrastructure/ConsoleBootstrap.js), Script por mongosh e Agregação por pipeline.

## 6. Metadados MongoDB

| Componente | Estado e limites confirmados |
| --- | --- |
| [MetadataCache](../../src/EsilvaSoft.SlopStudio.Application/Language/MetadataCache.cs) | TTL, stale, single-flight, backoff; LRU de 64 **entradas** de definição/índice/amostra por conexão; leituras usam lock, não são lock-free |
| [KnowledgeCatalog](../../src/EsilvaSoft.SlopStudio.Application/Language/KnowledgeCatalog.cs) | Query retorna da memória, mas Get padrão pode agendar Task.Run remoto; sem Changed/ResolveAsync no contrato atual |
| [CatalogModel](../../src/EsilvaSoft.SlopStudio.Application/Language/CatalogModel.cs) | Nomes reais: EditorDialects, CatalogScope, IDs string, CatalogQuery com ConnectionProfile; não criar cópias dos esboços |
| [MongoMetadataSource](../../src/EsilvaSoft.SlopStudio.Infrastructure/MongoMetadataSource.cs) | Listagens autorizadas, definição por coleção, índices, amostra nomes/tipos; reutiliza pool e ambiente |
| Tipos de coleção | ListCollectionNamesAsync conserva só nomes; Unknown até definição. Servidor oferece tipo com nameOnly; perda é da API escolhida |
| [CollectionSchema](../../src/EsilvaSoft.SlopStudio.Application/Language/CollectionSchema.cs) | Validator/índices/resultados/amostra, BSON/EJSON, arrays, enum limitado; builders limitados, mas Merge usa int.MaxValue |
| Explorer/workspace | Write-through, Connect/Disconnect e invalidação integrados; nomes vêm de cache Peek, não só da árvore |
| [Campos observados](../../src/EsilvaSoft.SlopStudio.Desktop/ViewModels/WorkspaceTabViewModel.Autocomplete.cs) | Memoizados por conjunto/perfil/alvo; primeira inferência ainda na UI; agregação reanalisa prefixo por captura |
| Amostragem | SampleSchemaAsync e SchemaSamplingProfileIds existem, sem controle visual. Ferramenta de validador continua lendo documentos completos |

Catálogo existe, mas ainda não substitui os geradores legados de sugestões.

## 7. IA e ONNX

[LocalAiModelService](../../src/EsilvaSoft.SlopStudio.Application/LocalAiModelService.cs) possui modelo único, fila, carga desacoplada, troca, cooldown e cancelamento. GenerateAsync chama EnsureLoadedAsync: checar Ready antes do await não garante que inline nunca carregue. Propor LoadedOnly verificado sob a fila e revisão do modelo.

[OnnxLocalModelRuntime](../../src/EsilvaSoft.SlopStudio.Infrastructure/OnnxLocalModelRuntime.cs) reutiliza modelo/tokenizer, cria GeneratorParams/Generator por geração, roda em Task.Run, cancela via terminate_session e faz fallback CPU automático quando permitido. Não usa diretamente OrtValue, pooling de tensores, streaming público ou KV entre pedidos. Não criar backend paralelo para cumprir nomes conceituais.

[ModelAdapters](../../src/EsilvaSoft.SlopStudio.Infrastructure/ModelAdapters.cs) e [DeepSeekModelTokenizer](../../src/EsilvaSoft.SlopStudio.Infrastructure/DeepSeekModelTokenizer.cs) isolam famílias. Builders FIM tokenizam prefixo/sufixo antes de cortar; Qwen recodifica marcadores; RequireFullContext pode repetir encode. A detecção de eco decodifica saída acumulada por token. TTFT medido começa **depois** da tokenização/criação do gerador, não equivale a tecla → ghost.

[AiProviderSelector](../../src/EsilvaSoft.SlopStudio.Infrastructure/AiProviderSelector.cs) ordena NPU/GPU/CPU compatíveis; explícito não faz fallback silencioso. [OnnxHardwareProbe](../../src/EsilvaSoft.SlopStudio.Infrastructure/OnnxHardwareProbe.cs) detecta disponibilidade, não homologa exportações. NPU depende de pacote/build/hardware e não foi validada nesta revisão.

## 8. Caches e riscos de fundo

| Caminho | Risco a reproduzir/medir e tratar incrementalmente |
| --- | --- |
| NameTable.Collect | Fallback substring varre escopo e não recebe cancellation token |
| MetadataCatalogSource._lastMerge | Uma memoização global; alternar abas/coleções ou recriar LocalSchemas força mescla/tabelas |
| Consulta de perfis | Nova NameTable e hash de identidade por consulta |
| MetadataCache.StartLoad | Uma tarefa por chave; single-flight não limita cargas de chaves distintas |
| Store versus LoadAsync | Write-through altera mesma Entry; carga antiga pode passar na guarda de referência e sobrescrever dado novo |
| SampleSchemaAsync | Grava depois do await sem guarda de geração da carga comum; testar disconnect/invalidate durante amostra |
| ConnectionIdentity | Hash truncado de URI salva + TargetHost; não representa revisão de ENV/segredo resolvido nem equivale à chave efetiva do MongoClientPool |
| LRU / mescla | 64 entradas não limita bytes; nomes fora do LRU; Merge sem teto conjunto |
| Changed | Só identifica perfil; falha sem valor não publica Changed. Planejar chave/geração/estado para lista não ficar carregando |

São riscos da inspeção estática, não novas medições ou falhas reproduzidas. Tarefas e testes em [execution-plan.md](execution-plan.md).

## 9. Concorrência e invariantes

Preservar versão monotônica inclusive edit/undo ABA, CTS por aba e descarte obsoleto. Invalidação forte comum remove Entry e descarta carga antiga, mas não interrompe individualmente o trabalho. Disconnect cancela token da conexão; cancelar espera de RefreshAsync não cancela carga compartilhada. Não prometer cancelamento nativo imediato.

Snapshot da sessão sem resultados/credenciais; alvo capturado antes de await; Explorer não executa pela seleção; escritas continuam protegidas, BSON/UUID e auditoria preservados.

## 10. Configuração e atalhos

[AutocompleteSettings](../../src/EsilvaSoft.SlopStudio.Core/Autocomplete.cs) v1 já possui Enabled/Mode/UseDictionary, atraso 50–2000 (padrão 150), contexto 64–8192 (2048), saída 1–256 (32), modelo/provider e opções de contexto. Não há flags independentes dos dois preemptivos ou registro de atalhos. Ctrl+Espaço/Tab/Esc são handlers; Ctrl+./Ctrl+; são planejados. [Migração e precedência](configuration.md).

## 11. Testes e evidências existentes

KnowledgeCatalogTests.cs também contém NameTableTests, MetadataCacheTests, SchemaBuilderTests, métricas, arquitetura e fonte Mongo. Reutilizar MetadataInvalidationTests, LanguageDefinitionTests, AggregationFieldInferenceTests, MongoCompletionTargetTests, PredictiveAutocompleteTests, AutocompleteReliabilityTests, AutocompleteUiTests e LocalAiModelServiceTests.

[Benchmarks](../../tests/EsilvaSoft.SlopStudio.Benchmarks/) já contém BaselineBenchmarks, CatalogBenchmarks, MemoryScenario e SyntheticWorkload. UnitTests/Language/Cases ainda é diretório proposto. Integração metadata pode ser ignorada sem SLOP_CONSOLE_MONGOD; modelos reais são opt-in. Histórico não encerra p95/p99, UI nativa, Linux, teclado/IME, leitor de tela ou CUDA/NPU.

## 12. Inventário: reutilizar, refatorar, substituir

| Decisão | Componentes |
| --- | --- |
| Reutilizar | DI/LiteDB, MongoClientPool, metadata/schema, LanguageDefinition, métricas/benchmarks, serviço IA, adapters/tokenizer, IncrementalCompletion |
| Evoluir com testes | Catálogo/cache (gerações, Peek, completude, limites), lexer/contexto/agregação, CompletionSession, builders FIM, invalidadores de cache |
| Substituir após paridade | MenuFlyout, geradores redundantes e overlay; manter fachadas até migrar último chamador, inclusive ghost |
| Não substituir | Acornima/Jint/mongosh, escrita/auditoria, autosave, proprietário LiteDB e backend GenAI |

Fase 1: **base implementada, aceite parcial**. Fases 2–5: propostas. Chave Customer.Id exige aspas; ghost inicial não corrige texto anterior ao cursor, lista explícita pode fazê-lo. [Decisões](decisions.md).


## 13. Complemento: aprendizado dinâmico

Inspecionado também Core/StructuredResults.cs: origem capturada, Method, Completeness e documentos EJSON já permitem hook de find. A memoização atual de campos não é aprendizado incremental persistente. Faltam fila dedicada, deltas estatísticos, FirstSeen/LastSeen, idempotência e repositório de learned schema. O proprietário LiteDB já registrado será estendido; não criar conexão adicional. [Especificação nova](schema-learning.md).
