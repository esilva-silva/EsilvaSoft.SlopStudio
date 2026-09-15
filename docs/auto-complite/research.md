# Pesquisa técnica

Síntese das abordagens de IDEs, editores, Language Servers e runtimes de inferência, com a implicação para esta IDE. Cada item termina em **Adotar**, **Adaptar** ou **Rejeitar (por ora)**. Fontes ao final; "verificado" indica consulta direta ao documento ou inspeção do assembly no cache NuGet desta máquina.

## 1. Completion providers e Language Server Protocol

- LSP 3.18 (verificado): `textDocument/completion` recebe `CompletionContext` com `triggerKind` (Invoked, TriggerCharacter, TriggerForIncompleteCompletions) e devolve `CompletionList` com `isIncomplete` e `itemDefaults`. `CompletionItem` separa `label`/`labelDetails`, `kind`, `sortText`, `filterText`, `insertText` com `insertTextFormat` (texto ou snippet), `textEdit` (inclusive `InsertReplaceEdit`), `additionalTextEdits`, `commitCharacters` e `data`. `completionItem/resolve` calcula campos caros (documentação) só para o item selecionado.
- LSP 3.18 introduz `textDocument/inlineCompletion` com `triggerKind` Invoked/Automatic e `InlineCompletionItem` (`insertText` texto ou snippet, `range`, `filterText`, `command`). A API de VS Code (`InlineCompletionItemProvider`) segue o mesmo modelo.
- Cancelamento: `$/cancelRequest`; o servidor pode responder `ContentModified` quando o documento mudou.

**Implicação.** A IDE é um processo único .NET; um servidor LSP separado adicionaria serialização JSON-RPC, processo e sincronização de documento sem benefício atual. **Adaptar:** contratos in-process que espelham os conceitos LSP (contexto de gatilho, lista incompleta, faixa insert/replace, resolve tardio, inline separado da lista). A equivalência mantém aberta a exposição futura via LSP.

## 2. Análise léxica, sintática, AST e parsing incremental

- **Roslyn** usa árvores imutáveis *red/green*: nós verdes compartilháveis entre versões, nós vermelhos com posição criados sob demanda; o parser é tolerante, inserindo tokens *missing* e agrupando tokens *skipped*, de modo que sempre há árvore completa para código incompleto.
- **tree-sitter**: parser GLR incremental com recuperação de erro, reaproveita subárvores inalteradas após edição. Exige biblioteca nativa por RID e bindings .NET de maturidade variável.
- **Acornima** (já presente via Jint e usado em `MongoCodeValidator`, `MongoCodeFormatter` e `ConsoleRuntime.GetStatement`): parser ECMAScript completo com AST ESTree e posições; lança `SyntaxErrorException` em código incompleto e não é incremental.
- O próprio highlighting da IDE já é um lexer tolerante com estado por linha e cache incremental.

| Opção | Tolerância | Incremental | Dependência | Decisão |
| --- | --- | --- | --- | --- |
| Acornima no cursor | Não | Não | Já existe (Infrastructure) | Rejeitar para contexto; manter para validação |
| tree-sitter | Sim | Sim | Nativa por RID | Rejeitar por ora (portabilidade, publicação ARM64) |
| Parser tolerante próprio sobre o lexer existente, reparse por statement | Sim, por construção | Por statement + cache de linhas | Nenhuma | **Adotar** ([AC-02](decisions.md)) |

## 3. Contexto do cursor e completion por escopo

- Roslyn infere o **tipo esperado** na posição (argumento, atribuição, retorno) e filtra/pondera símbolos por compatibilidade.
- Serviços de linguagem JSON orientados por **JSON Schema** (como o de VS Code) oferecem chaves e valores a partir do schema que se aplica ao nó sob o cursor. É exatamente o problema de MQL: `find(filtro)` tem um "schema" de filtro; `{ campo: { … } }` tem um "schema" de condição de campo.
- Completion por escopo: símbolos visíveis dependem da cadeia de escopos (pipeline, `let` de `$lookup`, `arrayFilters`).

**Adotar:** Context Engine com caminhada de *shapes* dirigida por dados (assinaturas de métodos e shapes de documentos MQL), em vez de regras codificadas por exemplo ([context-engine.md](context-engine.md)).

## 4. Ranking de sugestões

- Editores combinam pontuação de correspondência (prefixo, *camel humps*, subsequência com bônus em limites de palavra) com `sortText` do provider e memória de seleção recente (por exemplo, "selecionado recentemente por prefixo").
- Rankers aprendidos existem (modelos treinados em uso agregado), mas exigem dados de telemetria que esta IDE não coleta.

**Adaptar:** filtros rígidos + soma ponderada de sinais explicáveis, pesos calibrados offline com conjunto-ouro local ([ranking.md](ranking.md)). **Rejeitar por ora:** ranker aprendido.

## 5. Snippets

- Sintaxe TextMate/LSP: `$1`, `${1:placeholder}`, `${1|a,b|}`, `$0`, variáveis.
- AvaloniaEdit 12.0.0 (verificado no assembly) oferece `Snippet`, `SnippetReplaceableTextElement`, `SnippetBoundElement` (espelha outro placeholder), `SnippetCaretElement` e `SnippetInputHandler` (Tab entre elementos ativos, Enter/Esc encerram).

**Adotar:** snippets armazenados na sintaxe LSP no catálogo e convertidos para elementos do AvaloniaEdit na camada Desktop.

## 6. Lazy loading, resolve tardio e cache de metadados

- Resolve tardio (LSP) reduz custo por lista: documentação e exemplos só no item destacado.
- **Stale-while-revalidate** (RFC 5861): servir valor vencido imediatamente e revalidar em segundo plano — adequado a nomes de coleções que mudam raramente.
- **Single-flight**: pedidos concorrentes para a mesma chave compartilham uma carga.
- **Negative caching com backoff**: falha de permissão ou rede não repete a cada tecla.
- MongoDB (verificado): `listCollections` com `nameOnly: true` devolve só nome e tipo e, a partir do 5.0, não adquire locks; `authorizedCollections: true` com `nameOnly` permite listar sem o privilégio `listCollections`. Sem `nameOnly`, cada entrada traz `type` (collection/view/timeseries), `options` (inclusive `validator`), `info.readOnly` e `idIndex` — uma chamada por banco obtém todos os validators.
- `$jsonSchema` em validators fornece nomes, `bsonType`, `required`, `properties`, `items` e `enum` — metadado declarado, não dado.
- Amostragem com `$sample` + `$objectToArray` + `$type` permite calcular nomes e tipos **no servidor**, sem trafegar valores (profundidade fixa por pipeline).

**Adotar:** Metadata Cache com SWR, single-flight, TTL, backoff e invalidação por eventos; validator e índices como evidência de schema; amostragem de nomes/tipos no servidor somente sob ação explícita ou opt-in ([knowledge-catalog.md](knowledge-catalog.md)).

## 7. Cancelamento, debounce e concorrência

- "Última requisição vence": cada requisição carrega a versão do documento; respostas de versões antigas são descartadas mesmo que cheguem depois (equivale ao `ContentModified`).
- `CancellationToken` cooperativo em .NET; operações nativas precisam de ponte explícita (a IDE já usa `terminate_session` do GenAI).
- Debounce adaptativo ao ritmo de digitação reduz pedidos inúteis sem atrasar pausas reais.

**Adotar:** escopo de requisição por editor e modalidade (generalização de `CompletionSession`), carga de modelo desacoplada, verificação de versão na aplicação ([architecture.md](architecture.md#concorrência-e-cancelamento)).

## 8. Autocomplete inline/preemptivo

- Sugestão inline é distinta da lista: um único texto (ou poucos alternáveis), disparo automático, aceitação por Tab e aceitação parcial por palavra; ao digitar exatamente o início da sugestão, o editor apenas encurta o ghost sem nova requisição.
- Gatilhos típicos: pausa, fim de linha, abertura de bloco; inibição com lista aberta, seleção ativa, IME em composição.

**Adotar:** Inline Coordinator com camada determinística e camada IA, *typeahead* sobre o ghost e gatilhos validados por experimento ([preemptive-autocomplete.md](preemptive-autocomplete.md)).

## 9. Contexto para modelos pequenos e Fill-In-the-Middle

- FIM (Bavarian et al., 2022): o modelo recebe prefixo e sufixo e gera o meio; é o formato natural para completar no cursor.
- Qwen2.5-Coder (verificado no relatório técnico): FIM de arquivo `<|fim_prefix|>…<|fim_suffix|>…<|fim_middle|>` e formato de **repositório** com `<|repo_name|>` e `<|file_sep|>` precedendo o arquivo atual; pré-treino em nível de repositório com contexto de até 32 768 tokens.
- Recuperação de contexto (RepoCoder e trabalhos similares): trechos semelhantes ao código em edição melhoram a conclusão mais do que contexto indiscriminado.
- Modelos usam melhor informação no início e no fim do contexto do que no meio (Liu et al., 2023); em FIM o trecho mais relevante (fim do prefixo) já fica adjacente ao ponto de geração.
- Declarações de tipos (estilo TypeScript `.d.ts`) são abundantes em dados de treino de código, candidatas a representar schema com poucos tokens.

**Adaptar:** seleção de fatos por relevância, janela sintática, contratos de contexto versionados e avaliação empírica de formatos, preservando o contrato v1 dos pacotes SlopCoder ([ai-context.md](ai-context.md)).

## 10. ONNX Runtime GenAI

Verificado no assembly `Microsoft.ML.OnnxRuntimeGenAI` 0.15.2:

| Recurso | Presente | Uso proposto |
| --- | --- | --- |
| `Config.ClearProviders/AppendProvider/SetProviderOption/Overlay` | Sim | Já usado para providers e threads |
| `Generator.AppendTokens`, `GenerateNextToken`, `IsDone`, `GetSequence` | Sim | Já usado |
| `Generator.RewindTo`, `TokenCount` | Sim | Reuso de prefixo do KV cache entre pedidos (experimento) |
| `Generator.SetRuntimeOption("terminate_session")` | Sim | Já usado para cancelamento |
| `TokenizerStream` | Sim | Decodificação incremental, sem redecodificar a saída a cada token |
| `GeneratorParams.SetGuidance` (llguidance: JSON Schema, regex, Lark) | Sim na API | Pesquisa: restringir saída a sintaxe válida ou nomes do catálogo; disponibilidade no build nativo a verificar |
| `Adapters` + `SetActiveAdapter` (multi-LoRA) | Sim | Futuro: adapter MongoDB sobre modelo base |
| `EncodeBatch` | Sim | Tokenização de blocos estáveis |

Execution providers configuráveis pela IDE: CPU, DirectML, CUDA, QNN, OpenVINO, VitisAI. NPUs costumam exigir exportações específicas (quantização/formas estáticas) e não são garantidas por um pacote genérico. Resultados já registrados em [23](../23-onnx-slopcoder.md) e [26](../26-ia-local-multimodelo.md): um pacote INT8 exportado para CPU falhou em DirectML; pacotes exportados com provider `dml` funcionaram. **Adaptar:** manter seleção existente, homologar por pacote, política de latência por modalidade.

## 11. Otimização de inferência

- Latência percebida em autocomplete é dominada por **TTFT** (prefill) em prompts de 1–2 mil tokens e por tokens/s em gerações longas.
- Técnicas: reduzir tokens de entrada (seleção de fatos), reusar KV cache do prefixo estável, limitar tokens gerados, parar cedo em fronteira sintática, quantização adequada ao hardware, aquecimento após carga, evitar alocações por token.
- Medições existentes (pipeline externo, [23](../23-onnx-slopcoder.md)): 0.5B INT4 CPU ~206 ms TTFT; 1.5B INT8 CPU ~486 ms; 1.5B DML FP16 ~156 ms; média de 980 ms por autocomplete no harness com as DLLs da IDE. Servem de ordem de grandeza, não de meta.

## 12. Janela de contexto e fallback

- Orçamento por seções com prioridade e truncamento próprio (cortar fatos de baixa relevância antes do código próximo ao cursor).
- Estimar tokens por razão caracteres/token medida por modelo antes de tokenizar, evitando codificar dezenas de KiB para depois descartar.
- Fallback em camadas com mensagens explícitas; nenhuma falha de IA bloqueia edição.

## Resumo das decisões de pesquisa

| Técnica | Decisão |
| --- | --- |
| Servidor LSP separado | Rejeitar por ora; contratos in-process equivalentes |
| Parser tolerante próprio + lexer compartilhado | Adotar |
| tree-sitter | Rejeitar por ora |
| Shapes MQL dirigidos por dados | Adotar |
| Ranking ponderado explicável + calibração offline | Adotar |
| Ranker aprendido | Rejeitar por ora |
| Snippets LSP → AvaloniaEdit | Adotar |
| SWR, single-flight, TTL, backoff | Adotar |
| Amostragem de nomes/tipos no servidor | Adotar sob ação explícita/opt-in |
| Contratos de contexto versionados por modelo | Adotar |
| Formato de repositório Qwen para schema virtual | Avaliar na Fase 3 |
| Prefix cache via `RewindTo` | Experimento na Fase 4 |
| Decodificação restrita (`SetGuidance`) | Pesquisa; fora do aceite |
| `System.Diagnostics.Metrics` local | Adotar |

## Fontes

- [Language Server Protocol 3.18 — especificação](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.18/specification/) (verificado)
- [VS Code API — InlineCompletionItemProvider](https://code.visualstudio.com/api/references/vscode-api#InlineCompletionItemProvider) (verificado)
- [ONNX Runtime GenAI — API C#](https://onnxruntime.ai/docs/genai/api/csharp.html) (verificado; a página não lista todos os membros presentes no assembly 0.15.2)
- [ONNX Runtime GenAI 0.15.2 — Runtime options](https://github.com/microsoft/onnxruntime-genai/blob/v0.15.2/docs/RuntimeOptions.md) (verificado)
- [ONNX Runtime GenAI — exemplos Python (structured output com JSON Schema/Lark)](https://github.com/microsoft/onnxruntime-genai/blob/main/examples/python/README.md)
- [llguidance](https://github.com/guidance-ai/llguidance)
- [Qwen2.5-Coder Technical Report (arXiv 2409.12186)](https://arxiv.org/abs/2409.12186) (verificado)
- [Discussão sobre FIM + contexto de repositório no Qwen Coder](https://github.com/QwenLM/Qwen3-Coder/issues/343)
- [Efficient Training of Language Models to Fill in the Middle (arXiv 2207.14255)](https://arxiv.org/abs/2207.14255)
- [RepoCoder (arXiv 2303.12570)](https://arxiv.org/abs/2303.12570)
- [Lost in the Middle (arXiv 2307.03172)](https://arxiv.org/abs/2307.03172)
- [MongoDB — listCollections](https://www.mongodb.com/docs/manual/reference/command/listCollections/) (verificado)
- [MongoDB — Schema Validation / $jsonSchema](https://www.mongodb.com/docs/manual/core/schema-validation/)
- [RFC 5861 — stale-while-revalidate](https://www.rfc-editor.org/rfc/rfc5861)
- [Roslyn — syntax trees](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/work-with-syntax)
- [tree-sitter](https://tree-sitter.github.io/tree-sitter/)
- [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit)
- [Nielsen — Response Times: The 3 Important Limits](https://www.nngroup.com/articles/response-times-3-important-limits/)
- Inspeção local dos assemblies `Avalonia.AvaloniaEdit` 12.0.0, `Microsoft.ML.OnnxRuntimeGenAI.Managed` 0.15.2 e `Avalonia.Base` 12.1.2 (presença de `KeySymbol`/`PhysicalKey`) no cache NuGet.
