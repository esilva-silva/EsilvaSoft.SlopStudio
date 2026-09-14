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
| Camada 0 do inline | p95 ≤ 5 ms após o gatilho | Imperceptível | BenchmarkDotNet + Headless | 5 |
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
| MRR de uso real | posição média do item aceito na lista |

## Benchmarks

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
