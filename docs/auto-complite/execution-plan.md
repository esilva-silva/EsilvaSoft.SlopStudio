# Plano executável por agentes

Revisado em 15/09/2026. **Todas as tarefas abaixo estão pendentes**, salvo bases explicitamente existentes no inventário. Não iniciar implementação nesta revisão documental. Cada linha é um lote revisável; se exceder uma PR pequena, separar mantendo ID pai. Documentar evidência, riscos remanescentes e arquivos efetivamente alterados.

## Convenções e ordem

Componentes abaixo são relativos a `src/EsilvaSoft.SlopStudio.*`: **A** Application, **C** Core, **I** Infrastructure, **D** Desktop; **U** tests/EsilvaSoft.SlopStudio.UnitTests; **B** tests/EsilvaSoft.SlopStudio.Benchmarks. Arquivo/componente novo está marcado `(novo)`. Perfis completos em [agents](agents/README.md).

```text
G00 contratos / riscos / propriedade dos arquivos
 ├─ K11…K17 consolidação do catálogo existente
 ├─ L11…L16 schema learning persistente (novo requisito)
 └─ C21…C25 contexto (integra quando contratos K estiverem prontos)
          ↓
 T01…T08 tradicional explícito / editor comum
          ├─ P51…P53 tradicional preemptivo (sem IA)
          └─ A31…A34 dados IA + R41…R43 runtime
                     ↓ A41…A44 IA explícita
                     ↓ P54…P55 IA preemptiva
 P56…P58 integração híbrida e homologação
          ↓ G99 revisão final
```

Performance e Testing atuam em **todos** os lotes. Lotes sem dependência podem avançar juntos, mas não dois escritores no mesmo arquivo. DI, DTOs, WorkspaceTabView, metadata de modelos e proprietário LiteDB exigem reserva explícita do lote; Architecture revisa, não reescreve trabalho alheio em paralelo.

## Fase 1 — consolidar dados existentes

| ID / responsável | Arquivos/componentes | Depende | Resultado esperado | Testes obrigatórios | Critério de aceite |
| --- | --- | --- | --- | --- | --- |
| G00 Architecture | A/Language/CatalogModel.cs; architecture/decisions | — | Contratos de stamp/cobertura/acesso/lifetime e mapa de ownership aprovados | Revisão de dependências e teste de arquitetura existente | Sem abstrações duplicadas ou dependências IA no contexto/catálogo |
| K11 MongoDB Knowledge | A/Language/MetadataModel.cs, MetadataCache.cs; I/OperationEnvironment.cs | G00 | Geração opaca por origem, invalidação de perfil/ENV/credencial | Mesmo nome em hosts/bancos distintos; troca de ambiente | Não reutiliza schema de origem anterior; nenhuma URI em chave persistida |
| K12 MongoDB Knowledge | A/Language/MetadataCache.cs | K11 | Geração por chave para carga/write-through/amostra e opt-out | Fonte lenta após Put; disconnect/invalidate durante sample | Publicação tardia descartada; novo valor preservado |
| K13 MongoDB Knowledge | CatalogModel/KnowledgeCatalog/MetadataCache | K12 | Peek propagado, fila limitada e single-flight | Rajada de chaves, 50 consumidores mesma chave, cancelamento individual | Zero chamadas no automático/refiltro; limites de concorrência respeitados |
| K14 MongoDB Knowledge | I/MongoMetadataSource.cs; A/Language/MetadataModel.cs | K11 | Preservar kinds na listagem e estados de permissão | Fake e Mongo real restrito/view/timeseries | Tipo quando disponível sem carregar validators do banco |
| K15 MongoDB Knowledge | A/Language/CollectionSchema.cs, KnowledgeCatalog.cs, NameTable.cs | K12 | Mescla limitada por escopo/revisão e tabelas reaproveitadas | Abas alternadas, fontes disjuntas, cancelamento e bytes | Não reconstrói schema quente; limite conjunto/truncamento visível |
| K16 MongoDB Knowledge | CatalogModel/KnowledgeCatalog/MetadataModel | K13,K15 | Cobertura, truncamento e Changed terminal por chave/revisão | Falha sem valor, top-2 truncado, fontes concorrentes | Não confunde fresh com exaustivo; callback terminal chega |
| K17 Performance | B/CatalogBenchmarks.cs, MemoryScenario.cs; U/KnowledgeCatalogTests.cs | K14,K16 | Relatório multiaba/conexão, contenção, memória e fonte real | Protocolo performance.md; revisão Testing | Separar média/p95 e integrar só com limites demonstrados |

## Fase 1 — Schema Discovery / Schema Learning

Amplia a fase de dados; não reabre a implementação inteira do catálogo. [Especificação](schema-learning.md). A camada de contexto pode ser desenvolvida com snapshots falsos enquanto aprendizado é consolidado; aceite de dados inclui recuperação persistente.

| ID / responsável | Arquivos/componentes | Depende | Resultado esperado | Testes obrigatórios | Critério de aceite |
| --- | --- | --- | --- | --- | --- |
| L11 MongoDB Knowledge | C/StructuredResults.cs; A/SchemaLearningContracts (novo) | G00,K11 | Envelope BatchId/origem/completude e delta probabilístico por segmentos | Homônimos, literal com ponto, projeção/derived/unknown | Só origem confiável; modelo sem valores/URI |
| L12 MongoDB Knowledge | A/BackgroundSchemaAnalyzer (novo); CollectionSchema.cs | L11 | Extração limitada, tipos/presença/arrays/FirstSeen/LastSeen | UUID EJSON, string UUID, null/missing, polimorfismo, truncamento | Contagens por observação válidas, não documentos únicos |
| L13 MongoDB Knowledge | A/SchemaLearningService (novo); produtores WorkspaceService/Console; D/ViewModels/WorkspaceTabViewModel.Results (ponto a localizar) | L12 | TryEnqueue após entrega de find, fila limitada | Analyzer bloqueado, fila cheia, consulta cancelada, eventos repetidos | Resultado/UI não aguardam aprendizado; zero queries adicionais |
| L14 MongoDB Knowledge | A/ILearnedSchemaRepository (novo); I/LiteDbConnectionProfileRepository.SchemaLearning (novo partial); ServiceCollectionExtensions | L11,K12 | Transação de delta+BatchId, índices e migração versionada no dono único | Retry idempotente, concorrência autosave, migração/corrupção, erro disco | Reinício recupera estrutura; nenhum segundo LiteDatabase/valor salvo |
| L15 MongoDB Knowledge | A/SchemaLearningService, LearnedSchemaCatalogSource (novos); C/WorkspaceSession.cs | L13,L14,K16 | Hidratação por namespace, merge incremental, flags/limpeza/retention | Opt-out/disconnect/drop/rename durante commit; offline; restart | Delta tardio não grava; catálogo rápido em memória; erro visível |
| L16 Performance | B/SchemaLearningBenchmarks (novo); U/SchemaLearningTests (novo) | L15 | Relatório de custo, fila/memória/LiteDB, matriz Testing | Carga contínua, autosave/consulta concorrentes, 3 namespaces | TryEnqueue meta <1 ms, sem espera de resultado; limitações registradas |

## Fase 2 — contexto e tradicional explícito

| ID / responsável | Arquivos/componentes | Depende | Resultado esperado | Testes obrigatórios | Critério de aceite |
| --- | --- | --- | --- | --- | --- |
| C21 MongoDB Context | A/Language/Text + Syntax (novos); SyntaxHighlightingService | G00 | Snapshot/lexer tolerante compartilhado | Highlighting existente, Unicode/CRLF/comentários | Cores preservadas; sem tipo Avalonia na Application |
| C22 MongoDB Context | A/TolerantParser (novo) | C21 | Parser limitado de statement com missing/skipped/opaque | Truncamentos, regex/template/ASI, profundidade/linha gigante | Sempre contexto ou Unknown sem exceção/scan ilimitado |
| C23 MongoDB Context | A/CompletionContextEngine, NamespaceTargetResolver (novos) | C22,K16 | Alvo, papel, faixa e chave completa | MongoCompletionTargetTests + aliases/shadowing/ABA | Nenhum alvo herdado de outro statement/aba |
| C24 MongoDB Context | A/ShapeWalker/PipelineInfo (novos); AggregationFieldInference | C23 | Shapes e campos após stages incluindo facet/count | AggregationFieldInferenceTests; lookup estrangeiro; tipos BSON | Paridade de casos existentes sem falso campo certo |
| C25 MongoDB Context | A/SyntaxTreeCache (novo); B/LexerParserBenchmarks (novo) | C24 | Reparse por statement/checkpoints limitado | Diferencial edição aleatória; 1 MB; múltiplas abas | Igual ao completo no subconjunto; orçamento medido |
| T01 Traditional Completion | D/SyntaxHighlighting/MongoTextEditor; A/EditorRequestScope (novo) | C21,G00 | Adaptador snapshot e stamp/cancelamento comum | A/B tardio, binding/undo/execução/sessão | Captura antes de await; sem regressão de rascunhos |
| T02 Traditional Completion | A/CompletionService, TraditionalCompletionProvider (novos) | C24,K16,T01 | Candidatos por shape/fontes com truncamento | Fonte fora do escopo, candidate além do corte | Query limitada e sem IA; no refiltro só Peek |
| T03 Traditional Completion | A/CompletionRanker/RankingProfile (novos) | T02 | Ranking comum com normalização/top-2 | Corpus separado, MRR/top-1/top-5, empate, tipos | Ordinal estável; sem candidato inválido para shape |
| T04 Traditional Completion | A/SnippetTemplate; D/SnippetInserter (novos) | T02 | Snippets/edições e aspas/UUID | Faixas, caracteres escapados, placeholders/undo | Uma unidade de undo; preservar BSON e não executar |
| T05 Traditional Completion | D/CompletionWindowPresenter (novo), App.axaml | T03,T04 | Lista filtrável, resolve tardio, falhas | Navegação, foco, resize/zoom e 18 PNGs | Seleção preservada; imagens reais inspecionadas |
| T06 Traditional Completion | D/WorkspaceTabView.axaml.cs; serviços MQL/Console legados | T05,C25 | Adaptar menu e fachadas sem perder ghost/execução | Suites legadas e chamadores por rg | Remover só caminhos já substituídos |
| T07 Traditional Preemptive | D/InlineCompletionPresenter (novo), MongoTextEditor, InlineCompletionTextBlock | T01,G00 | Protótipo/presenter comum sem ONNX; contrato para 4/5 | Caret fim da linha, multilinha/sufixo, scroll, copy/undo; PNGs | Ghost não altera documento; manter fallback visual se necessário |
| T08 Traditional Completion | C/Autocomplete.cs/WorkspaceSession.cs; D/EditorCommandDispatcher e Preferências | T05,G00 | Ctrl+./alias/Ctrl+;, flags/migração; Ctrl+; integrado no A42 | Configuração ausente/false, erro persistência, atalhos/foco | Sem reset de sessão; política conforme configuration.md |

## Fase 3 — dados para IA

| ID / responsável | Arquivos/componentes | Depende | Resultado esperado | Testes obrigatórios | Critério de aceite |
| --- | --- | --- | --- | --- | --- |
| A31 AI Completion | A/AutocompleteContextBuilder + IAiContextContract (novo) | C24,G00 | Serializador v1 preservado Qwen/DeepSeek | Prompts independentes byte a byte | Pacotes antigos mantêm contrato |
| A32 AI Completion | A/RelevantContextSelector/EditorWindowBuilder (novos) | A31,K16 | Fatos relevantes do alvo/learned/opções | 40 coleções, lookup, privacidade/opt-out | Somente fontes permitidas; sem valores de resultados |
| A33 AI Completion | A/AiBudget/AiContextBuilder (novos); interface tokenizer via R41 | A32,R41 | Contagem final com reserva e cache correto | Encode concatenado vs blocos, Unicode, 10k budgets | Prompt dentro do limite, determinístico e sem tokenizer concorrente |
| A34 AI Completion | B/AiContextEvaluationHarness (novo); I/LocalModelCatalog via dono ONNX | A33 | v1 e um contrato alternativo avaliados; B–E experimentais | Dois modelos quando disponíveis; indisponíveis registrados | Nenhuma promoção de formato sem relatório |

## Fase 4 — runtime e IA explícita

| ID / responsável | Arquivos/componentes | Depende | Resultado esperado | Testes obrigatórios | Critério de aceite |
| --- | --- | --- | --- | --- | --- |
| R41 ONNX Runtime | A/ILocalAiModelService/LocalAiModelService; I/ModelAdapters/LocalModelCatalog; C/LocalAi.cs | G00 | Política AllowLoad/LoadedOnly, revisão/motivos tipados, tokens/contrato | Modelo errado sob gate, erro contexto, pacote antigo | LoadedOnly não carrega/troca; runtime sem regra MongoDB |
| R42 ONNX Runtime | I/OnnxLocalModelRuntime, ModelAdapters, DeepSeekModelTokenizer; builders FIM | R41 | Marcadores em cache, prompt exato, streaming/decode incremental | Cancelamento, abandono de enumeração, Unicode, chat, real/fake | Gate/lifetime corretos; sem decode completo por token |
| R43 ONNX Runtime | I/OnnxLocalModelRuntime; B/AiRuntimeHarness (novo) | R42 | Experimento KV/prefix cache por provider | Greedy independente, terminate/reset/modelo/chat | Default desligado; só habilitar com equivalência e ganho |
| A41 AI Completion | A/AiGenerationPipeline/AiCompletionProvider/CompletionOutputProcessor (novos) | A33,R42 | Provider explícito e geração compartilhada | Sufixo/shape/nomes novos/privacidade | Sem duplicar runtime; candidato seguro |
| A42 AI Completion | D/WorkspaceTabView/dispatcher/presenter comum | A41,T07,T08 | Ctrl+;, indicador e prévia validada | Tab/Esc/Enter/foco/PNG, UI responsiva | Um presenter, uma edição/undo; nenhum autoexecute |
| A43 AI Completion | A/AiCompletionProvider; LocalAiModelService via ONNX | A42 | Fallback/timeout/motivos sem strings mágicas | Cada linha da matriz fallback; flags desligadas | Falha não bloqueia; fallback respeita TraditionalEnabled |
| A44 Performance | B/AiRuntimeHarness; U/LocalAiModelServiceTests | A43 | Perfil completo por modelo/provider e relatório | Fila, tokenização, prefill/decode, CPU/chat concorrente | p95 e hardware efetivo; fake não prova real |

R43 é experimento opcional e não bloqueia A41/5.2. LoadedOnly deve estar correto antes da IA automática.

## Fase 5 — dois preemptivos e integração

| ID / responsável | Arquivos/componentes | Depende | Resultado esperado | Testes obrigatórios | Critério de aceite |
| --- | --- | --- | --- | --- | --- |
| P51 Traditional Preemptive | A/InlineCompletionCoordinator/InlineGate (novos) | T01,T07,T08,C25 | Geração por editor, gating/coalescing/fila curta | IME, seleção, cursor sem edição, rajada | Uma pendência; nenhum evento inútil gera modelo |
| P52 Traditional Preemptive | A/TraditionalPreemptiveCompletionProvider (novo) | P51,T03,K17 | Ranker compartilhado, Peek, confiança, cache | Sem IA, top-2 truncado, ambiguidade, snippet | Funciona totalmente sem modelo; zero I/O |
| P53 Traditional Preemptive | D/presenter e WorkspaceTabView.Autocomplete | P52 | Ghost/typeahead/Tab/Esc/undo | Candidato concluído versus em voo, ABA, imagens | Edição visualizada igual à aplicada; obsoleto descartado |
| P54 AI Preemptive | A/AiPreemptiveCompletionProvider (novo) | A43,A44,P51,P53 | Pipeline comum + LoadedOnly/Background | Tradicional false, troca/chat, token ignorado | IA independente, nenhuma carga/troca automática |
| P55 AI Preemptive | A/InlineAiPolicy/ModelLatencyProfile (novos) | P54 | Debounce/deadline/histerese/limites | Clock falso, 20 teclas, perfil desconhecido, timeout | No máximo uma inferência por pausa e dentro da política |
| P56 Traditional Preemptive | A/InlineCompletionCoordinator | P53,P55 | Arbitragem sequencial estável | Tradicional forte e IA tardia; Esc/Changed | Zero IA após candidato forte; nenhuma troca no padrão |
| P57 Testing | U/HybridCompletionTests (novo); docs/15 | P56,T08,L16 | Matriz quatro providers, flags, learning/refresh/undo | A/B, multiaba, migração, fallback, arquivo sem valores | Todos os invariantes observáveis cobertos sem espelhar implementação |
| P58 Performance | B/InlineBenchmarks (novo); docs/performance/testing | P57 | Uso real, latência/CPU/aceite/reversão, migração final | PNGs com dono Desktop; Mongo/modelos/nativo disponíveis | Separar pendências; extensão paralela só experimento desligado |
| G99 Architecture | Diff integrado, docs/10/12/15/17/21/24/26 | P58,T06,A34 | Revisão de dependências, remoção segura, avaliação de contexto IA e evidências | Restore/build/test por AGENTS; inspeções proporcionais | Concluir só escopo efetivamente verificado; menu legado adaptado, formato IA avaliado ou pendência explícita; sem alegar homologação ausente |

## Contrato de execução e handoff

Agente recebe ID, commit base, arquivos reservados, contratos upstream e cenário de aceite. Devolve diff pequeno, testes/relatório, incompatibilidades e decisão pendente. Sem alterar contrato upstream unilateralmente: propor a Architecture, serializar mudança e só então retomar consumidores. Testes e Performance revisam desde G00; agente implementador escreve testes locais, Testing adiciona cenários independentes.

Cada merge de lote exige docs coerentes e `node scripts/build-docs-index.cjs`. Implementação de UI exige PNGs reais; sessão/cache/contexto exige falha/concorrência/recuperação. Ausência de MongoDB/modelo/hardware não se transforma em passe: registrar o gate não executado. Não trocar assert/golden para mascarar regressão.
