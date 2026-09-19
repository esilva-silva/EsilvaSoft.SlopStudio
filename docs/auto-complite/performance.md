# Desempenho, instrumentação e benchmarks

## Princípio

Nenhum número final é fixado sem medir a implementação atual e as novas. Esta página define **o que medir, como medir e orçamentos provisórios**. A Fase 1 começa com a medição do código atual; os orçamentos são revistos com esses dados e registrados aqui antes do aceite de cada fase.

## Baseline do código atual (Fase 1)

| Medida | Caminho atual | Método |
| --- | --- | --- |
| Trabalho na UI por tecla e por movimento de cursor | `EditorCompletionChanged` → `CaptureAutocompleteRequest` → `GetImmediateCompletion` | Cronômetro em build de diagnóstico + Headless com documentos de 1 KiB, 16 KiB, 64 KiB e 1 MB |
| Resolução de alvo | `MongoCompletionTarget.Resolve` (re-lex do prefixo) | BenchmarkDotNet |
| Inferência de campos | `InferFieldPaths` com 1–8 documentos de 1–64 KiB | BenchmarkDotNet |
| Dicionário básico | `BasicAutocompleteProvider.GetCompletion` | BenchmarkDotNet |
| Privacidade | `CompletionPrivacy` sobre contextos de 1–65 KiB | BenchmarkDotNet |
| Chave de cache | JSON + SHA-256 do request | BenchmarkDotNet |
| Menu | `ShowSuggestions` até abrir | Headless |
| Highlighting por tecla | `RefreshHighlighting` + `Highlight` | BenchmarkDotNet + Headless |
| IA | Tokenização, TTFT, total, tokens/s, working set | `Explicit` com modelos reais |

## Baseline medida — Fase 1

Execução de 14/09/2026 em AMD Ryzen 9 7900 (12 núcleos), Windows 11 25H2, .NET 10.0.12 x64, BenchmarkDotNet 0.15.8 com `--job short --inProcess` (3 iterações; o erro fica entre 5% e 50% da média, então as médias indicam ordem de grandeza, não p95). Médias em µs, alocação por chamada entre parênteses. Reproduzir:

```bash
dotnet run -c Release --project tests/EsilvaSoft.SlopStudio.Benchmarks -- --filter "*" --job short --inProcess
```

```bash
dotnet run -c Release --project tests/EsilvaSoft.SlopStudio.Benchmarks -- memory
```

### Caminho atual por evento do editor

Script sintético do Console com o cursor no fim; oito documentos de resultado do mesmo tamanho.

| Operação | 1 KiB | 16 KiB | 64 KiB |
| --- | ---: | ---: | ---: |
| `MongoCompletionTarget.Resolve` (re-lex do prefixo) | 29,3 (111 KB) | 872,6 (1,63 MB) | 0,000002 — não resolve¹ |
| Highlight completo do documento | 23,2 (93 KB) | 488,0 (1,35 MB) | 3 581,0 (5,42 MB) |
| `InferFieldPaths`, 8 documentos | 49,3 (48 KB) | 651,1 (431 KB) | 2 485,6 (1,66 MB) |
| `AutocompleteContextBuilder.Build` | 30,3 (49 KB) | 30,8 (55 KB) | 29,7 (55 KB) |
| `BasicAutocompleteProvider` | 23,0 (33 KB) | 57,8 (120 KB) | 58,3 (120 KB) |
| `CompletionPrivacy` sobre prefixo + sufixo | 0,2 (2 KB) | 2,2 (33 KB) | 13,9 (132 KB) |
| `GetCompletionAsync` sem dicionário (JSON + SHA-256) | 13,0 (25 KB) | 18,0 (37 KB) | 18,0 (37 KB) |

¹ `MongoCompletionTarget.Resolve` devolve `null` quando o prefixo passa de 65 536 caracteres: acima de 64 KiB o alvo simplesmente não é resolvido, e as sugestões de coleção e campo deixam de ter escopo. É um limite funcional, não um ganho de desempenho.

Leitura:

- Resolução de alvo e highlight crescem com o documento inteiro. Em 16 KiB, a soma das operações síncronas desta tabela chega a cerca de 1,5 ms e 3 MB alocados por evento. Em 64 KiB, só o highlight passa de 3,5 ms, acima do orçamento de 2 ms por tecla. É esse o custo que a análise por statement da Fase 2 precisa remover.
- A inferência de campos dos resultados custa até 2,5 ms e agora é memoizada por conjunto de resultados na aba (Incremento 1.4); antes da Fase 1 ela se repetia a cada captura.
- Construção de contexto, dicionário e chave de cache ficam limitados pela janela de contexto e não crescem com o documento.
- O tempo real no dispatcher (`ui.autocomplete.dispatcher_time`) ainda não foi medido em Headless nem nativo; a soma acima é uma estimativa de componentes.

### Catálogo

Consultas respondidas da memória: 10, 100 e 1 000 coleções × 100, 1 000 e 10 000 campos, com o schema já mesclado.

| Consulta | 100 campos | 1 000 campos | 10 000 campos |
| --- | ---: | ---: | ---: |
| Campo por prefixo (`campo0001`) | 3,4 (5 KB) | 16,5 (5 KB) | 145,8–150,8 (5 KB) |
| Campo por camel humps (`cn`, 200 candidatos) | 11,7 (43 KB) | 40,6–43,8 (175 KB) | 40,7–41,4 (175 KB) |
| Campos aninhados (`Cliente.`) | 1,5 (4 KB) | 1,6 (4 KB) | 1,6 (4 KB) |
| Operador por prefixo (`e`) | 1,1 (3 KB) | 1,1 (3 KB) | 1,1 (3 KB) |

| Consulta | 10 coleções | 100 coleções | 1 000 coleções |
| --- | ---: | ---: | ---: |
| Coleção por prefixo (`colecao00`) | 1,7 (5 KB) | 10,1–10,8 (40 KB) | 26,1–26,6 (40 KB) |

| Estrutura | 100 nomes | 1 000 nomes | 10 000 nomes |
| --- | ---: | ---: | ---: |
| Construir `NameTable` | 18,4 (37 KB) | 230,3 (354 KB) | 4 793,2 (3,52 MB) |
| `GetCollections` com `Peek` | 0,096 (72 B) | 0,096 (72 B) | 0,095 (72 B) |
| `ConnectionIdentity.From` (SHA-256) | 0,25 (464 B) | 0,25 (464 B) | 0,24 (464 B) |

Leitura:

- **Média abaixo de 1 ms; p95 pendente.** A pior consulta, com 10 000 campos, fica em 0,15 ms.
- **Prefixo com poucas correspondências é linear.** Quando prefixo e camel humps devolvem menos que o máximo pedido, `NameTable.Collect` completa com uma varredura de substring sobre a tabela inteira; daí os 146 µs com 10 000 campos. Cabe no orçamento. Se o ranking da Fase 2 exigir mais folga, o caminho é limitar a varredura por tempo ou por tamanho de consulta, não remover o fallback.
- **Camel humps aloca 175 KB** por consulta com 200 candidatos, acima dos 64 KB por tecla do orçamento da Fase 2. A origem exata (candidatos, detalhes ou conjunto de vistos) ainda não foi perfilada; a Fase 2 deve refiltrar a lista aberta em vez de consultar de novo a cada tecla.
- **Construir a tabela custa mais que consultá-la.** Com 10 000 nomes são 4,8 ms, por isso tabelas e o schema mesclado são construídos uma vez por versão do cache e nunca por tecla.
- Ler o cache com `Peek` custa ~100 ns sem alocar além do view.

### Memória

Cenário `memory`: 1 000 coleções × 1 000 campos com validator em todas, 80 coleções visitadas com definição e amostra explícita, LRU padrão de 64 entradas por conexão, uma consulta de 200 campos.

| Parcela | Retido |
| --- | ---: |
| Nomes de coleções | 0,1 MB |
| Definições com validator (LRU) | 31,8 MB |
| Schemas amostrados (mesmo LRU; a amostra desloca definições) | −2,3 MB |
| Schema mesclado e tabelas da consulta | 0,4 MB |
| **Total** | **29,9 MB** |

A primeira implementação carregava os validators do banco inteiro em uma chamada e retinha 99,7 MB (69,9 MB de JSON de validator fora do LRU). O tipo e o validator passaram a ser carregados por coleção dentro do LRU; o orçamento de 64 MB é atendido com folga.

### Revisão dos orçamentos

| Orçamento | Resultado | Decisão |
| --- | --- | --- |
| Consulta ao catálogo, 10 000 campos, p95 ≤ 1 ms | 0,15 ms de média; p95 não medido | Mantido como meta, aceite pendente |
| Memória do catálogo ≤ 64 MB | 29,9 MB | Mantido |
| Trabalho de UI por tecla p95 ≤ 2 ms | Estimativa de ~1,5 ms em 16 KiB e mais de 3,5 ms em 64 KiB no caminho atual | Mantido como meta da Fase 2; medição no dispatcher pendente |
| Alocação por tecla ≤ 64 KB | 3 MB em 16 KiB no caminho atual; 175 KB por consulta de humps | Mantido; exige refiltro sem nova consulta e análise por statement na Fase 2 |
| Construção de contexto ≤ 64 KiB p95 ≤ 5 ms | Contexto legado ~30 µs; resolução + highlight chegam a 1,4 ms em 16 KiB e o highlight sozinho a 3,6 ms em 64 KiB | Mantido; o parser da Fase 2 é medido contra estes números |

Pendentes: p95/p99 com job completo, medição Headless e nativa do dispatcher, documento de 1 MB e uma segunda máquina de referência.

## Orçamentos provisórios

| Métrica | Orçamento provisório | Racional | Medição | Fase |
| --- | --- | --- | --- | --- |
| Trabalho de autocomplete na UI por tecla | p95 ≤ 2 ms, p99 ≤ 8 ms | Fração de um quadro de 16,7 ms | `ui.autocomplete.dispatcher_time` | 2 |
| Construção de contexto, documento ≤ 64 KiB, reparse de statement | p95 ≤ 5 ms | Muito abaixo de 100 ms (resposta "instantânea") | BenchmarkDotNet | 2 |
| Construção de contexto, documento 1 MB | p95 ≤ 20 ms | Análise limitada ao statement | BenchmarkDotNet | 2 |
| Consulta ao catálogo, coleção com 10 000 campos | p95 ≤ 1 ms | Busca binária | BenchmarkDotNet | 1 |
| Ranking, 200 candidatos → top 100 | p95 ≤ 2 ms | Heap parcial | BenchmarkDotNet | 2 |
| Tecla → lista visível (quente) | p95 ≤ 50 ms | Percepção de imediato | Headless + nativo | 2 |
| Refiltro com lista aberta | p95 ≤ 8 ms | Um quadro | Headless | 2 |
| Alocação por tecla, caminho sem IA | ≤ 64 KB | Pressão de GC | `MemoryDiagnoser` | 2 |
| Seleção de fatos + builder | p95 ≤ 10 ms | Pequeno frente ao TTFT | BenchmarkDotNet | 3 |
| Tokenização de até 2 048 tokens | p95 ≤ 5 ms (Qwen nativo); DeepSeek .NET a medir | Não dominar a latência | `Explicit` | 3 |
| Camada 0 do inline | p95 ≤ 5 ms após o gatilho | Imperceptível | BenchmarkDotNet + Headless | 5 — **atendida em 18/09/2026** (p95 0,009–0,068 ms); ver [seção acima](#camada-0-do-inline-51--medição-de-18092026) |
| Edição → ghost (5.1, tradicional) | p95 ≤ 20 ms | Percepção de imediato | Headless, `TimeProvider` falso | 5 — **medida e NÃO atendida em 18/09/2026** (excedente de 17,6–27,1 ms sobre o debounce, dominado pela resolução do temporizador do Windows); meta mantida sem ajuste |
| Camada 1 do inline, pausa → ghost | p95 ≤ TTFT medido + 150 ms e ≤ 600 ms; acima, a camada é desativada para o par modelo/hardware | TTFT de referência em [23](../23-onnx-slopcoder.md) | `Explicit` | 5 |
| `Ctrl+;` → primeiro texto | p95 ≤ 1 s com indicador; timeout 10 s | Ação explícita com feedback | `Explicit` | 4 |
| Memória do catálogo, 1 000 coleções × 1 000 campos com LRU | ≤ 64 MB | Aplicação desktop | `MemoryDiagnoser` + working set | 1 |
| Itens exibidos por lista | 100 (200 ranqueados) | Usabilidade | Configuração | 2 |
| Contexto máximo de IA | 64–8192 tokens (existente), padrão 2048 | Janela do modelo | Preferência | — |
| Tokens gerados inline | 24 (avaliar 16/32) | TTFT + decode | `Explicit` | 5 |

Referências existentes (pipeline externo, não metas): 0.5B INT4 CPU TTFT ~206 ms; 1.5B INT8 CPU ~486 ms; 1.5B DML-FP16 ~156 ms; média de 980 ms por autocomplete pelas DLLs da IDE.

## Instrumentação

`System.Diagnostics.Metrics` com `Meter("EsilvaSoft.SlopStudio.Autocomplete")` e `ActivitySource` homônimo para perfis de desenvolvimento. `IAutocompleteDiagnostics` continua existindo para eventos técnicos.

| Nome da meta | Instrumento | Tipo | Tags permitidas |
| --- | --- | --- | --- |
| `Completion.Requested` | `completion.requested` | Counter | `modality`, `trigger`, `dialect` |
| `Completion.Returned` | `completion.returned` | Counter | `modality`, `completeness`, `count_bucket` |
| `Completion.Accepted` | `completion.accepted` | Counter | `modality`, `kind`, `source`, `rank_bucket` |
| `Completion.Cancelled` | `completion.cancelled` | Counter | `modality`, `reason` |
| `Completion.Latency` | `completion.latency` | Histogram (ms) | `modality` |
| `ContextBuild.Duration` | `context_build.duration` | Histogram (ms) | `dialect`, `reparse`, `size_bucket` |
| — | `catalog.query.duration` | Histogram (ms) | `kind_group`, `completeness` |
| — | `ranking.duration` | Histogram (ms) | `candidate_bucket` |
| — | `metadata.refresh.duration` | Histogram (ms) | `scope`, `outcome` |
| — | `metadata.cache.lookup` | Counter | `scope`, `result` (`fresh`/`stale`/`miss`) |
| `AICompletion.Requested` | `ai_completion.requested` | Counter | `modality`, `provider` |
| `AICompletion.Generated` | `ai_completion.generated` | Counter | `modality`, `provider`, `outcome` |
| `AICompletion.Accepted` | `ai_completion.accepted` | Counter | `modality`, `partial` |
| `AICompletion.Cancelled` | `ai_completion.cancelled` | Counter | `modality`, `reason` |
| `AICompletion.Latency` | `ai_completion.latency` | Histogram (ms) | `modality`, `provider` |
| — | `ai_context_build.duration` | Histogram (ms) | `contract` |
| `Tokenization.Duration` | `tokenization.duration` | Histogram (ms) | `tokenizer` |
| `Inference.Duration` | `inference.duration` | Histogram (ms) | `provider`, `modality` |
| — | `inference.ttft` | Histogram (ms) | `provider`, `modality` |
| — | `inference.tokens_per_second` | Histogram | `provider` |
| — | `inference.prompt_tokens`, `inference.generated_tokens` | Histogram | `modality` |
| — | `prefix_cache.reused_tokens` | Histogram | `provider` |
| — | `ui.autocomplete.dispatcher_time` | Histogram (ms) | `handler` |
| — | `inline.*` | Counter/Histogram | `tier`, `trigger`, `outcome` |

Regras:

- **Proibido** em tags ou eventos: texto do editor, prompts, nomes de campos/coleções/bancos/conexões, caminhos de modelo, valores, mensagens nativas brutas.
- `provider` é o nome do execution provider (`cpu`, `dml`, `cuda`); família de modelo no máximo (`qwen2`, `llama`), nunca a pasta.
- Coleta local: `MeterListener` em memória para a janela de diagnóstico e para testes; `dotnet-counters` em desenvolvimento. Não há upload.

### Métricas de qualidade

| Métrica | Definição |
| --- | --- |
| Taxa de aceite | aceitos ÷ exibidos, por modalidade, tipo e posição no ranking |
| Aceite parcial | caracteres aceitos ÷ caracteres sugeridos |
| Reversão | aceitos desfeitos em até 5 s ÷ aceitos |
| Ruído inline | exibidos sem interação ÷ exibidos |
| Cancelamento | cancelados ÷ solicitados |
| Posição aceita em uso real | média da posição do item aceito; não é MRR |

## Benchmarks

### Estado da medição de ranking — 15/09/2026 (superada, ver baseline pós-ADR-040 abaixo)

`CompletionRankerBenchmarks` foi ajustado para não ser `sealed`, requisito do BenchmarkDotNet. A execução curta do ranking (20, 200 e 2 000 candidatos) ainda não produziu números: o gerador encontra projetos `EsilvaSoft.SlopStudio.Benchmarks.csproj` homônimos em worktrees `.claude` e recusa escolher um. A tentativa fora do sandbox confirmou que não é limitação de permissão. Executar o job em um checkout sem esses worktrees; resultados `NA` não são métricas e não podem validar o orçamento de ranking.

### Baseline pós-ADR-040 (extração de Autocomplete.Core) — 17/09/2026

Medição em AMD Ryzen 9 7900 3.70GHz, Windows 11 25H2, .NET SDK 10.0.401, BenchmarkDotNet 0.15.8, job padrão. O bug de descoberta de projeto descrito acima (múltiplos `.csproj` homônimos por causa dos worktrees `.claude/worktrees/*` de agentes) persiste — não foi corrigido, apenas contornado com `--inProcess`, que evita a resolução externa de projeto. Isso não é uma correção definitiva do bug do gerador; enquanto existirem esses worktrees, `--inProcess` continua necessário para obter qualquer número.

```bash
dotnet run -c Release --project tests/EsilvaSoft.SlopStudio.Benchmarks -- --filter "*CompletionRanker*|*MongoLexer*|*SyntaxHighlighting*" --inProcess
```

| Benchmark | Candidatos/tamanho | Tempo médio | Alocação |
| --- | --- | ---: | ---: |
| `CompletionRanker.Rank` | 20 candidatos | 2,243 µs | 2,8 KB |
| `CompletionRanker.Rank` | 200 candidatos | 26,889 µs | 29,16 KB |
| `CompletionRanker.Rank` | 2 000 candidatos | 110,695 µs | 215,49 KB |
| `MongoLexer.Tokenize` | 1 MB | 2 447,482 µs | 21 B (praticamente zero-alloc) |
| `SyntaxHighlightingService` completo | 1 MB | 14 009,777 µs | 18 593,56 KB |
| `SyntaxHighlightingService` incremental | 1 MB | 2 629,424 µs | 9 530,10 KB |

Leitura: é a primeira baseline numérica de ranking desde a reorganização física do ADR-040; não é o job completo sem `--job short`/`--inProcess` exigido pelo protocolo da Fase 2 (seção "Fase 2 — protocolo e estado" abaixo), portanto ainda não aprova o gate de orçamento de ranking (p95 ≤ 2 ms para 200 candidatos) — a média de 26,889 µs para 200 candidatos está bem abaixo do orçamento, mas p95/p99 seguem pendentes. `MongoLexer.Tokenize` e `SyntaxHighlightingService` não tinham baseline registrada em 1 MB antes desta medição.

### Correção de alocação em `CompletionRanker` (lote 5A) — 17/09/2026

`CompletionRanker.TryMatch` varria a string duas vezes para candidatos que casavam por camel humps (`HasCamelHumps` seguido de `CamelHighlights`) e alocava um `TextSpan[]` de realce por candidato analisado, inclusive para os que nunca sobreviviam ao heap de top-K. As duas varreduras foram fundidas em `TryCamelHumps(value, prefix, highlights: TextSpan[]?)`: com `highlights` nulo (fase de match) faz uma única varredura sem alocar; com o array informado (materialização final), preenche os realces no mesmo laço. A construção de `Highlights` foi adiada para depois de `heap.Sort`, via `BuildHighlights`, que recalcula o realce determinístico (valor, prefixo, tipo de match) só para os itens que efetivamente saem — os descartados do heap nunca pagam esse custo. `Candidate` deixou de carregar `Highlights`/`Score` no `CompletionItem` clonado; o heap compara por `(Score, Match, Label, SymbolId, Ordinal)` e só o `record` final ganha `with { Score, Highlights }`.

Medição com `dotnet run -c Release --project tests/EsilvaSoft.SlopStudio.Benchmarks -- --filter "*CompletionRanker*" --inProcess` (mesma máquina/config da tabela acima; `--inProcess` continua necessário por causa dos worktrees `.claude` descritos no estado de 15/09/2026, ainda não removidos):

| Candidatos | Alocação antes (baseline 17/09) | Alocação depois (lote 5A) |
| --- | ---: | ---: |
| 20 | 2,8 KB | 2,8 KB |
| 200 | 29,16 KB | 22,13 KB |
| 2 000 | 215,49 KB | 21,74 KB |

Leitura: já estava dentro do orçamento de 64 KB/tecla nesta baseline específica (a cifra de 175 KB citada nas seções de catálogo acima é de `CatalogQuery`/`NameTable`, um componente diferente, fora do escopo deste lote), mas a alocação crescia linearmente com o número de candidatos analisados porque cada um pagava o realce mesmo quando descartado; agora fica efetivamente constante em relação a `CandidateCount` (só o top-K materializa `Highlights`), o que é a correção estrutural pedida. São médias de `MemoryDiagnoser` (alocação gerenciada por operação), não p95; o job completo de p95 por lote segue pendente pelo mesmo motivo dos worktrees `.claude` não removidos. Testes funcionais (`CompletionRankerTests`, incluindo ordem, realces, desempate por `SymbolId` e invariância a `LabelDetail`) continuam verdes sem alteração de asserção.

### Projeto

```text
tests/EsilvaSoft.SlopStudio.Benchmarks/
  Current/BaselineBenchmarks.cs            caminhos atuais listados na baseline
  Catalog/CatalogQueryBenchmarks.cs
  Catalog/MetadataCacheBenchmarks.cs       single-flight, troca de snapshot, LRU
  Syntax/LexerParserBenchmarks.cs          tamanhos e posições de edição
  Context/ContextEngineBenchmarks.cs       fixtures de testing.md
  Completion/RankingBenchmarks.cs
  Ai/ContextSelectionBenchmarks.cs
  Ai/AiRuntimeHarness.cs                   Explicit, modelo por variável de ambiente
  Data/SyntheticCatalogGenerator.cs        semente fixa, sem dados reais
```

BenchmarkDotNet com `MemoryDiagnoser`; nova dependência justificada por ser ferramenta de medição fora do produto, fixada em `Directory.Packages.props` com lockfile.

### Matriz de escala

| Dimensão | Valores |
| --- | --- |
| Coleções por banco | 10, 100, 1 000 |
| Campos por coleção | 100, 1 000, 10 000 |
| Profundidade de aninhamento | 1, 4, 12 |
| Proporção de arrays de documentos | 0%, 20% |
| Metadados disponíveis | nenhum, só nomes, nomes + índices, validator, schema completo, `Loading`/`Stale` |
| Tamanho do documento | 1 KiB, 16 KiB, 64 KiB, 1 MB |
| Posição da edição | início, meio, fim |
| Candidatos para ranking | 20, 200, 2 000 |

### IA

`AiRuntimeHarness` executa aquecimento + N medições por célula e registra, quando aplicável:

| Métrica | Fonte |
| --- | --- |
| Context Build | Cronômetro em seleção + builder |
| Tokenization | Cronômetro em encode |
| Time To First Token | `ModelGenerationResult.TimeToFirstToken` |
| Total Generation | `Elapsed` |
| Tokens/second | Decode após o primeiro token |
| Memory | Working set antes/depois; VRAM não medida |
| Prefix cache | Tokens reaproveitados |

Matriz: modelos disponíveis × hardware (CPU, DML; CUDA/NPU quando houver) × contexto (256, 512, 1024, 2048) × geração (8, 16, 32, 64) × contratos A–E × prefix cache ligado/desligado. Saída JSON com p50/p95/p99 em `TestResults/autocomplete-ai-*.json`, com máquina, build (`Cpu`/`WinML`/`Cuda`) e versão do pacote.

### Relatórios e regressão

- Resultados BenchmarkDotNet exportados em JSON como artefato; resumo em Markdown anexado à PR que altera o caminho medido.
- Máquinas de referência: a máquina documentada (AMD Ryzen 9 7900, Radeon RX 7800 XT) e uma máquina modesta a definir na Fase 1.
- CI executa um subconjunto curto sem bloquear (ruído de runners); gates de aceite usam as máquinas de referência.
- Regressão de p95 acima de 20% em duas execuções consecutivas exige investigação registrada.

## Limites configuráveis

Seguindo o padrão de `SyntaxHighlightingOptions`, `LanguageServiceOptions` centraliza constantes internas:

| Constante | Inicial |
| --- | --- |
| `MaximumCandidates` | 200 |
| `MaximumDisplayedItems` | 100 |
| `FullParseCharacters` | 64 KiB |
| `MaximumNesting` | 512 |
| `SchemaMaximumNodes` | 10 000 |
| `SchemaMaximumDepth` | 12 |
| `SchemaCollectionsPerConnection` | 64 (LRU) |
| `ContextWaitBeforeOpen` | 50 ms |
| `InlineMaximumLines` | 8 |
| `UsageHalfLife` | 30 min |


## Protocolo revisado de medição e gates

Números anteriores são histórico de 14/09/2026, não nova execução nem comparação causal de UI. Highlight completo em worker não deve ser somado automaticamente ao trabalho síncrono do dispatcher. Medir evento real e cada segmento; tempo do TTFT legado exclui encode/criação do gerador.

Baseline reutiliza arquivos reais na raiz de Benchmarks: BaselineBenchmarks.cs, CatalogBenchmarks.cs, MemoryScenario.cs, SyntheticWorkload.cs. Subpastas acima são extensões, não estrutura já entregue. Rodar release, lockfile/backend fixados, registrar commit, SO, runtime, CPU/RAM/provider/modelo, energia e interferências.

Separar frio (carga/primeira mescla/parser completo) de quente (cache, reparse/refiltro) e máquina potente de máquina modesta. Para percentis interativos: 5 aquecimentos + pelo menos 200 eventos determinísticos por cenário, 3 rodadas; registrar amostras e p50/p95/p99, não inferir percentil das 3 médias de job short. Para IA cara, mínimo 30 gerações por célula, p99 apenas exploratório até amostra maior; cancelados/timeouts são contados separadamente, não excluídos para melhorar latência.

Matriz adicional obrigatória: alternar 2/10 abas com schemas distintos; 4 conexões; 200 chaves frias para fila; write-through concorrente; união de 3 fontes disjuntas; campo raro além dos 200 primeiros; 1 MB sem fronteira de statement; rajada de 20 teclas; typeahead; timeout/troca de modelo durante geração.

Tradicional preemptivo: computação p95 ≤ 5 ms e evento→ghost p95 ≤ 20 ms, quente, zero chamadas Mongo/IA. IA preemptiva: última edição→candidato validado ≤ 600 ms provisórios, incluindo debounce/fila/encode/prefill/decode/UI; abster-se após prazo. Não confundir 0–20 ms pretendidos com resultado medido. Relatar CPU média/pico, alocação, GC, working set e inferências evitadas; energia quando houver instrumento disponível, sem inventar estimativa.

Gates: zero aplicação obsoleta, zero I/O automático, nenhum vazamento de valores; orçamentos quantitativos revisados em máquina identificada. Regressão >20% no p95 em duas rodadas exige investigação. Nenhum gate é aprovado por mudar golden/assertion ou usar média como p95. Riscos sem reprodução ficam identificados como tal. Performance e Testing acompanham cada lote; P58 só consolida evidência.

### Fase 2 — protocolo e estado

A plataforma de aceite é **Windows x64**. Para documentos de até 64 KiB, registrar p95 de trabalho de UI por tecla (≤ 2 ms), construção de contexto (≤ 5 ms), tecla → lista visível (≤ 50 ms) e refiltro com a lista aberta (≤ 8 ms), além da alocação por tecla no caminho sem IA (≤ 64 KB). Para 1 MB, registrar construção de contexto com p95 ≤ 20 ms. Cada resultado deve identificar commit, configuração Release, SO, runtime, CPU, memória, tamanho/posição da edição e estado frio ou quente.

O **job completo** do BenchmarkDotNet, sem `--job short`, produz a evidência quantitativa de aceite e regressão. O **job curto** serve somente para diagnóstico rápido e ordem de grandeza; suas médias não são p95 e não aprovam gate. Sequência de reprodução:

```bash
dotnet restore EsilvaSoft.SlopStudio.slnx --locked-mode
dotnet build EsilvaSoft.SlopStudio.slnx --no-restore
dotnet test EsilvaSoft.SlopStudio.slnx --no-build --no-restore
dotnet run -c Release --project tests/EsilvaSoft.SlopStudio.Benchmarks -- --filter "*"
```

Diagnóstico curto, sempre rotulado como não conclusivo:

```bash
dotnet run -c Release --project tests/EsilvaSoft.SlopStudio.Benchmarks -- --filter "*" --job short --inProcess
```

Estado atual: lexer/highlighting e ranking possuem benchmarks no projeto; parser/cache ainda não têm benchmark dedicado, e contexto, lista visível/refiltro e alocação por tecla ainda não têm medição final da Fase 2. Permanecem pendentes o job completo em Windows x64, a coleta Headless e nativa da UI e a cobertura de 1 KiB, 16 KiB, 64 KiB e 1 MB nas posições início/meio/fim. Os números da Fase 1 acima continuam apenas como baseline histórica; nenhum gate da Fase 2 está declarado aprovado.


## Benchmark do Schema Discovery / Schema Learning

Estender B com SchemaLearningBenchmarks: resultado→TryEnqueue (meta p95 <1 ms), extração/bytes por lote, drop/backlog, CPU/GC, memória retida, delta/commit e tempo sob lock LiteDB com autosave concorrente. Comparar find→resultado com aprendizado ligado/desligado em mesma carga; investigar regressão p95 >5%. Dataset sintético com BSON polimórfico, projeção, campos com ponto e arrays extensos; reinício/hidratação fria e prefixo quente. Sem novos acessos Mongo e sem leitura LiteDB por tecla. [Limites e protocolo](schema-learning.md); nenhum número é medição desta revisão.

## Remedição da alocação por tecla — 17/09/2026

A cifra de **175 KB por consulta de camel humps**, registrada nas seções de catálogo acima e repetida na matriz de
orçamentos, **não se reproduz** na medição direta feita nesta data. Ela foi reaferida componente a componente, com
`GC.GetAllocatedBytesForCurrentThread()` sobre 100–200 repetições após aquecimento, em vez de por benchmark agregado:

| Componente medido | Cenário | Alocação por operação |
| --- | --- | --- |
| `NameTable<T>.Collect` por prefixo | 10 000 nomes, `maximum` 100 | **1 888 B** |
| `NameTable<T>.Collect` por camel humps | 10 000 nomes, `maximum` 100 | **1 864 B** |
| `NameTable<T>.Collect` sem casamento | 10 000 nomes, varre o escopo inteiro | **1 880 B** |
| `KnowledgeCatalog.Query` sobre a linguagem embarcada | `MaximumCandidates` 200 | **10 396 B** |
| `CompletionRanker.Rank` | 200 candidatos → top 100 | 22,13 KB |
| `CompletionRanker.Rank` | 2 000 candidatos → top 100 | 21,74 KB |

> **Correção desta própria seção — 18/09/2026.** O parágrafo abaixo estava ERRADO por escolha de cenário. As
> medições acima usam o **catálogo de linguagem embarcado** (462 símbolos) e a busca de nomes isolada, e nesse
> recorte o orçamento realmente sobra. Mas o cenário que o número original de 175 KB descreve é outro: **campos
> vindos da fonte de metadados em coleção grande**. Remedido nesse cenário, com 200 candidatos:
>
> | Campos na coleção | Alocação por consulta |
> | --- | --- |
> | 100 | **43,39 KB** |
> | 1 000 | **174,72 KB** |
> | 10 000 | **174,72 KB** |
>
> Portanto **o orçamento de 64 KB por tecla CONTINUA ESTOURADO** a partir de ~1 000 campos, e a cifra de ~175 KB do
> documento original está correta — não era stale nem mal atribuída à toa.
>
> **Causa identificada:** `MetadataCatalogSource.Describe(FieldNode)` monta o texto de apresentação de cada campo
> (`List<string>`, interpolação e `string.Join`) **por candidato**, inclusive para os ~100 que o ranqueamento vai
> descartar. É a mesma classe de defeito já corrigida no `CompletionRanker` com os realces, no componente vizinho.
> A correção natural é diferir `Detail` para os sobreviventes do top-K, já que `CatalogSymbol` guarda o `FieldNode`
> em `Field` e pode compor o texto sob demanda. **Não aplicada nesta entrega.**

Ou seja, o orçamento de **≤ 64 KB por tecla no caminho sem IA está cumprido** em todos os componentes determinísticos
medidos, com folga de pelo menos 3×. A busca de nomes, que a redação anterior apontava como culpada, aloca menos de
2 KB; o que de fato escala é a materialização de um `CatalogCandidate` por candidato na consulta, e ainda assim dentro
do orçamento. **Nenhuma otimização foi aplicada ao `NameTable` porque a medição não sustentou a premissa de que havia
o que otimizar** — mudar código que já mede bem só acrescentaria risco.

As três primeiras linhas viraram teste permanente em
`tests/EsilvaSoft.SlopStudio.UnitTests/NameTableAllocationTests.cs`, que falha se a alocação por consulta ultrapassar
64 KB. A guarda é de alocação, não de latência: uma regressão de alocação não aparece como erro, só como digitação
engasgada, e por isso é afirmada em teste e não apenas observada em benchmark.

## Camada 0 do inline (5.1) — medição de 18/09/2026

Computação determinística de `TraditionalPreemptiveCompletionProvider`, catálogo de linguagem embarcado: **p50 0,002 ms, p95 0,009 ms, máx 0,013 ms.** Com 200 campos de schema (teto do modo automático): **p50 0,044 ms, p95 0,068 ms, máx 4,24 ms.** A meta de camada 0 (p95 ≤ 5 ms após o gatilho) **é atendida** nos dois cenários.

### Edição → ghost — meta de 20 ms NÃO comprovadamente atendida

Medido em Headless com debounce configurado em 50 ms: o excedente sobre o debounce ficou em **17,6–27,1 ms** (primeira sugestão do processo: 92 ms, 112 ms, 108 ms; aquecida: 77 ms, 68 ms, 77 ms). O trabalho de geração propriamente dito é ~0,07 ms (linha acima); o excedente é dominado pela **resolução do temporizador do Windows (~15,6 ms)** somada a dois saltos de despachante (dispatch da UI). **Registrado como não atendido, com o número medido acima. A meta provisória de 20 ms não foi ajustada** — a lacuna é do mecanismo de temporização/despacho, não da geração da sugestão.

## Cache de tokens da Fase 2 — ganho medido em 18/09/2026

BenchmarkDotNet, AMD Ryzen 9 7900, Windows 11 25H2, .NET 10.0.12. Análise por tecla voltou ao patamar da baseline anterior ao cache (1 MiB/meio: 7,78 ms → 7,38 ms). A análise repetida da mesma versão de documento — refiltro com a lista aberta, sem reprocessar o texto — ficou substancialmente mais barata:

| Cenário | Ganho |
| --- | --- |
| 64 KiB, edição no fim | 0,37× do tempo anterior |
| 1 MiB, edição no meio | 0,14× do tempo anterior |
| 1 MiB, edição no início | 71 ns (efetivamente reaproveitado sem reprocessar) |

### Pendências honestas desta remedição

- **p95/p99 continuam não medidos.** Os números acima são alocação por operação, e média de alocação não é p95 de
  latência. O job completo do BenchmarkDotNet foi iniciado após a remoção dos worktrees de `.claude/worktrees`, mas não
  concluiu dentro desta sessão e foi interrompido; os gates de latência da Fase 2 (UI por tecla p95 ≤ 2 ms, contexto
  p95 ≤ 5 ms, catálogo p95 ≤ 1 ms, ranking p95 ≤ 2 ms, tecla→lista p95 ≤ 50 ms, refiltro p95 ≤ 8 ms) **seguem sem
  evidência de aprovação**. Nenhum deles pode ser declarado cumprido com o que existe hoje.
- A linha da tabela de orçamentos que cita "3 MB em 16 KiB no caminho atual" não foi reaferida nesta data e permanece
  como estava.
- O estouro do highlighting (3,5 ms em 64 KiB contra orçamento de 2 ms por tecla) **não foi corrigido** e permanece
  pendente, fora do escopo desta entrega.

## Baseline medida — Fase L (schema learning) — 19/09/2026

Execução de 19/09/2026 em AMD Ryzen 9 7900 3,70 GHz (12 núcleos físicos, 24 lógicos), Windows 11 25H2
(10.0.26200.9457), .NET SDK 10.0.401 / runtime .NET 10.0.12 x64 RyuJIT x86-64-v4, BenchmarkDotNet 0.15.8 com
`--job short --inProcess` (3 aquecimentos + 3 iterações, 1 lançamento) — o mesmo protocolo da
[baseline da Fase 1](#baseline-medida--fase-1), com a mesma limitação: com N = 3 a margem de 99,9% fica
frequentemente acima da própria média, então **as médias indicam ordem de grandeza e o desvio padrão é o que se
publica; p95/p99 continuam não medidos**. Desvio padrão entre parênteses; alocação por operação na última coluna.
Reproduzir:

```bash
dotnet run -c Release --project tests/EsilvaSoft.SlopStudio.Benchmarks -- --filter "*SchemaLearning*" --job short --inProcess
```

Os lotes sintéticos são gerados por `tests/EsilvaSoft.SlopStudio.Benchmarks/SchemaLearning/SyntheticLearningWorkload.cs`
e a forma que eles realmente produzem — profundidade, número de caminhos e tamanho por documento — é afirmada em
`SyntheticLearningWorkloadTests.cs`, para que o benchmark não possa mentir sobre o cenário que mediu.

### Análise de lote (`BackgroundSchemaAnalyzer.Analyze`)

Limites reais do analisador, conferidos no código: profundidade 12, 10 000 nós por lote, 1 000 elementos de array por
campo por documento, 64 KiB por documento, 32 documentos por lote.

| Forma do lote | Documentos / caracteres | Caminhos | Média | Alocado |
| --- | --- | ---: | ---: | ---: |
| Pequeno (4 campos × 2 níveis) | 8 / 3 098 | 11 | 24,59 µs (0,08) | 70,24 KB |
| Médio (12 campos × 5 níveis) | 32 / 77 478 | 66 | 612,85 µs (11,41) | 1 655,76 KB |
| Grande (24 campos × 12 níveis, nomes distintos por documento) | 32 / 388 112 | 9 570 | 20 137,66 µs (497,29) | 21 334,41 KB |
| Estourando (30 campos × 14 níveis) | 32 / 553 524 | 13 858 pedidos → 10 000 gravados, `IsTruncated` | 22 529,60 µs (1 016,17) | 23 531,33 KB |

**Aceitável para o caminho em que roda.** O analisador só é chamado pelo worker único do `SchemaLearningCoordinator`
(`Task.Run` no construtor), nunca pelo despachante da UI: 20–23 ms por lote no teto dos orçamentos é custo de fundo, e
o caso que estoura custa apenas ~12% a mais que o que encosta no limite — o truncamento é barato, o tamanho é que não é.
**Pendência honesta registrada e não corrigida nesta entrega:** 21 MB alocados por lote no teto, com coletas de Gen2,
vêm de `GetOrCreateAccumulator` materializar `LearnedFieldPath` + `ToCanonicalId()` + `Convert.ToBase64String` por
caminho **por documento**, e de `Append` copiar o vetor de segmentos a cada nível. É pressão de GC de fundo, não de
tecla, mas é o maior número desta medição.

### Commit em LiteDB (`ILearnedSchemaRepository.ApplyAsync`)

Banco LiteDB temporário real, namespace já existente (mede-se a atualização, não a inserção), BatchIds distintos para
que cada commit seja de fato aplicado e não caia no atalho de idempotência.

| Operação | 8 campos | 64 campos | 512 campos |
| --- | ---: | ---: | ---: |
| `ApplyAsync` (transação curta) | 124,10 µs (7,20) | 355,47 µs (0,44) | 2 509,45 µs (4,98) |
| Alocado por commit | 117,14 KB | 603,85 KB | 4 518,28 KB |
| `ReadAvailabilityAsync` (leitura do namespace) | 38,82 µs (0,20) | 130,72 µs (0,18) | 948,64 µs (2,58) |
| Alocado por leitura | 57,92 KB | 312,94 KB | 2 350,44 KB |

**Aceitável para o caminho em que roda, com uma ressalva explícita.** O commit acontece no mesmo worker de fundo,
depois da análise, e nunca no caminho da consulta do usuário. Mas ele roda sob o `_gate` do **único** dono do arquivo
LiteDB, o mesmo do autosave de sessão e do repositório de perfis: um delta de 512 caminhos segura esse lock por ~2,5 ms.
Com no máximo 32 lotes na fila e um worker só, o pior caso contíguo é da ordem de dezenas de milissegundos de lock —
aceitável hoje, e a razão pela qual aumentar o paralelismo do worker não é uma otimização inócua.

### Hidratação do catálogo aprendido (`LearnedSchemaCatalogSource`)

Repositório em memória: mede-se o custo do próprio catálogo (montar o `CollectionSchema` e varrer os filhos), sem
disco. A latência fria ponta a ponta percebida é esta **somada** à leitura do namespace da tabela anterior.

| Operação | 50 campos aprendidos | 500 campos | 5 000 campos |
| --- | ---: | ---: | ---: |
| `Collect` com LRU quente | 104,41 µs (2,69) | 65,27 µs (17,28) | 93,32 µs (1,00) |
| Alocado por consulta quente | 52,82 KB | 98,99 KB | 98,99 KB |
| Primeira `Collect` (devolve `Loading`) | 1,12 µs (0,61) | 0,59 µs (0,26) | 2,12 µs (1,49) |
| Frio até servível (hidratação completa) | 156,50 µs (3,40) | 462,07 µs (13,57) | 13 701,57 µs (75,94) |
| Alocado na hidratação | 182,94 KB | 1 174,58 KB | 11 241,32 KB |

**Rápido o bastante para não atrasar a lista, com duas ressalvas.** A consulta quente fica em 0,05–0,10 ms, dentro do
orçamento de 1 ms por consulta ao catálogo da Fase 1 — e a variação entre 50 e 500 campos é menor que a margem de
N = 3, ou seja, o que estes números sustentam é a ordem de grandeza (dezenas de µs), não uma curva. A primeira consulta
custa ~1–2 µs e devolve `Loading`: **o contrato de nunca fazer I/O na thread chamadora está medido, não só afirmado**.
As ressalvas: (a) a consulta quente aloca 53–99 KB, o que **estoura o orçamento de 64 KB por tecla** a partir de ~500
campos aprendidos, exatamente a mesma classe de defeito já registrada para `MetadataCatalogSource.Describe` na
[remedição de 17/09/2026](#remedição-da-alocação-por-tecla--17092026) e não corrigida aqui; (b) com 5 000 campos
aprendidos a hidratação fria custa ~13,7 ms de CPU mais a leitura de disco (~0,95 ms), uma vez por namespace e por
ciclo do LRU de 8 entradas — aceitável como custo único, e é o número a vigiar se o teto de campos aprendidos subir.

### Vazão e descarte da fila (`SchemaLearningCoordinator`)

Capacidade padrão de 32 lotes, worker único, repositório falso com latência artificial (o descarte é o objeto da
medição; I/O real tornaria o resultado dependente do estado do SSD). Uma invocação é a rajada inteira mais a drenagem.

| Rajada | Latência do commit | Aceitos | Descartados | Processados | Média | Alocado |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 64 | 0 ms | 32 | 32 (50,0%) | 32 | 981,8 µs (116,65) | 2,22 MB |
| 64 | 2 ms | 32 | 32 (50,0%) | 32 | 505 114 µs (3 753,76) | 2,24 MB |
| 512 | 0 ms | 33 | 479 (93,6%) | 33 | 1 018,0 µs (74,55) | 2,27 MB |
| 512 | 2 ms | 33 | 479 (93,6%) | 33 | 515 236 µs (432,02) | 2,26 MB |

A média das linhas de 2 ms **não mede a fila**: 32 commits de `Task.Delay(2 ms)` viram ~500 ms por causa da resolução
do temporizador do Windows (~15,6 ms), o mesmo mecanismo já registrado na
[medição de edição → ghost](#edição--ghost--meta-de-20-ms-não-comprovadamente-atendida). O número útil destas linhas é
a coluna de descarte, e ela não muda com a latência do repositório.

**Aceitável, e o resultado é informativo.** O produtor nunca bloqueia: `TryEnqueue` é síncrono e a rajada de 512
lotes é absorvida em ~1 ms. Mas **o descarte começa no 33º lote da rajada independentemente da velocidade do
repositório** — mesmo com commit instantâneo só 33 foram aceitos, porque o produtor é ordens de grandeza mais rápido
que analisar + comitar. Isso é o comportamento especificado (fila cheia descarta o lote novo, nunca o que está em
processamento, e aprendizado nunca faz parte do sucesso da consulta), mas significa, em número: **uma navegação
rápida por muitas páginas contribui com cerca de 32 lotes, não com todas as páginas vistas**. A UI não pode prometer
"aprendi tudo o que você viu"; a redação de "observações de documentos analisadas" já é a correta por outro motivo e
também cobre este.

### Pendências desta medição

- p95/p99 não medidos: `--job short` com N = 3 dá margens de 99,9% maiores que a própria média em vários casos.
- Nenhuma otimização foi aplicada. As duas candidatas com evidência são a chave Base64 por caminho por documento no
  analisador (21 MB/lote no teto) e os 99 KB por consulta quente do catálogo aprendido, esta última já com causa
  conhecida e compartilhada com `MetadataCatalogSource`.
- A medição não cobre o `find` → resultado com aprendizado ligado contra desligado na mesma carga, nem o lock do
  LiteDB com autosave concorrente, pedidos na seção
  [Benchmark do Schema Discovery / Schema Learning](#benchmark-do-schema-discovery--schema-learning).
