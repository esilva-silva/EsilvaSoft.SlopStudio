# Estratégia de testes

## Princípios

Herdados do [`AGENTS.md`](../../AGENTS.md) e de [08 — Testes](../08-testes-e-qualidade.md):

- Cenários observáveis e fixtures independentes; nada de testes que apenas repetem a implementação.
- Golden files e asserções não são alterados para esconder regressão; mudança de expectativa é justificada na PR.
- Headless não substitui MongoDB real, layouts de teclado nativos, IME, leitor de tela ou Linux. Pendências são registradas na [matriz de validação](../15-matriz-de-validacao.md).
- Alterações de contexto e sessão exigem testes de falha, concorrência e recuperação; alterações visuais exigem inspeção dos PNGs gerados.

## Níveis

| Nível | Alvo | Ferramenta | Execução |
| --- | --- | --- | --- |
| Unitário | `Application.Language.*` | NUnit | Suíte regular |
| Propriedade / diferencial | Parser, orçamento de tokens, ranking | NUnit com geradores determinísticos (sementes fixas, sem nova dependência) | Suíte regular |
| Contrato | Catálogo ↔ runtime do Console; prompt `editor-context-v1` byte a byte; schema do arquivo de linguagem e do metadata do modelo | NUnit + Jint com host falso | Suíte regular |
| Arquitetura | Referências proibidas entre camadas | NUnit + reflexão | Suíte regular |
| Integração MongoDB | `IMongoMetadataSource`, amostragem, permissões | Fixture existente `SLOP_CONSOLE_MONGOD` | `Explicit`/condicional |
| UI Headless | Lista, snippets, arbitragem, ghost, atalhos | Avalonia.Headless + PNG | Suíte regular |
| IA com fakes | Provider, fallback, cancelamento, streaming | `ILocalModelRuntime`/`ITokenizer` falsos | Suíte regular |
| IA com modelos reais | Contratos, hardware, latência | Variáveis de ambiente | `Explicit` |
| Benchmarks | Latência e memória | BenchmarkDotNet + harness | Manual/CI não bloqueante |
| Homologação manual | Layouts ABNT2/US, IME, leitor de tela, Linux, MongoDB real, GPU | Checklist | Registro na matriz |

## Fixtures de contexto

Arquivos `tests/EsilvaSoft.SlopStudio.UnitTests/Language/Cases/*.case`, lidos por um runner parametrizado:

```text
// case: filter-operator-object-uuid
// mode: Console
// target: servidor-alfa/Projetos/Clientes
// schema: schemas/clientes.json
db.Clientes.find({
    Id: {
        |
    }
})
// expect.role: PropertyKey
// expect.kinds: QueryOperator
// expect.parentField: Id
// expect.top5: $eq, $in, $ne, $nin, $exists
// expect.absent: $size, $match
```

Os schemas das fixtures são sintéticos e ficam em `Language/Cases/schemas/`. Cada categoria abaixo tem casos positivos e negativos.

| Categoria | Casos mínimos |
| --- | --- |
| Raiz e membros | `db.`, `db.Clientes.`, cursor após `find({})`, `getConnection("P").`, `ConnectionPool.P.b.`, `db["x-y"].`, `getSiblingDB("b").` |
| Filtros | Raiz do filtro, objeto de operadores tipado, `$and`/`$or` com array, `$elemMatch`, caminho com ponto entre aspas e sem aspas (recuperação), `$expr` |
| Updates | `$set`, `$push` com `$each`, posicionais, pipeline de update com stages permitidos |
| Agregação | Chave de stage, `$match`, `$project`, acumuladores em `$group`, `$lookup` com `foreignField` da coleção `from`, `$$` e variáveis de `let`, campos após `$project`/`$group`/`$unwind` |
| Cursor e opções | `sort`, `project`, `hint` com índices, `collation` |
| Atlas/MongoDB Search | `$search` com `compound`, `text.path` |
| Não completável | Comentário, número, regex, string comum |
| Alvo | Casos atuais de `MongoCompletionTargetTests` (explícito, comentado, variável dinâmica, múltiplos statements) + alias `const` |
| Documento incompleto | Delimitadores ausentes, vírgula sobrando, string não fechada |
| Dialetos | Mesmo texto em Console, Script e Agregação com superfícies distintas |

Cobertura obrigatória dos [casos de 06 — Editor](../06-editor-bson-e-uuid.md#casos-obrigatórios): `{ "sta` em filtro; `{ "age": { "$g`; update no primeiro nível; pipeline após `$project`; `$group` e estágio seguinte; `$lookup`; `$$`; `items.$[item]`; pipeline de update; sem conexão; campo com ponto literal.

## Context Engine e parser

- Cada fixture valida papel, tipos esperados, alvo e confiança, campo pai, faixa de substituição e estilo de aspas.
- **Propriedade:** para cada script do corpus, todo truncamento produz árvore sem exceção e com spans monotônicos.
- **Diferencial:** sequência aleatória de edições (sementes fixas) — a árvore incremental é equivalente à árvore de análise completa.
- **Limites:** profundidade 512, documento de 1 MB, linha longa: análise dentro do orçamento e contexto do statement do cursor correto.
- **Highlighting:** `SyntaxHighlightingTests` e `SyntaxHighlightingUiTests` inalterados após extrair o lexer.

## Knowledge Catalog

| Aspecto | Testes |
| --- | --- |
| Conexões, bancos, coleções | Consulta por escopo e prefixo; tipos collection/view/timeseries; nomes que exigem indexador |
| Campos | Validator `$jsonSchema` (properties, required, items, enum), índices, resultados (wrappers EJSON não viram campos), amostra |
| Aninhados e arrays | Caminhos com ponto, arrays de documentos, arrays de escalares, tipos polimórficos, truncamento por limite |
| Operadores | Filtro por shape, dialeto, versão do servidor; compatibilidade de tipo |
| Linguagem | Validação do arquivo embutido; teste de contrato com o bootstrap do Console; projeção igual ou superconjunto de `MongoSyntaxVocabulary` |
| Cache | `Query` sem I/O (fonte falsa conta chamadas); single-flight com N consultas concorrentes → 1 chamada; stale servido e revalidado; backoff com `TimeProvider` falso; LRU respeitado |
| Invalidação | Cada operação DDL por `WorkspaceService` e pelo Console invalida exatamente as chaves previstas; perfil editado/desconectado limpa a conexão |
| Refresh | Explorer escreve no cache; recarga manual; `authorizedCollections` após erro de permissão |
| Privacidade | Snapshot serializado não contém nenhum valor presente nos documentos da fixture; amostra só com ação explícita/opt-in |

## Ranking

- Conjunto-ouro com MRR, top-1 e top-5 por shape, registrado como baseline na Fase 2 e usado como gate de não regressão.
- Invariantes: item inválido para o shape nunca no top-5; ordem determinística entre execuções; penalidade de tipo aplicada; uso recente altera a ordem apenas dentro do escopo correspondente.

## Completion Provider

| Aspecto | Testes |
| --- | --- |
| Prefixos | Exato, caixa, camel humps, `$` opcional, subsequência |
| Contexto | Só fontes necessárias consultadas (fontes falsas registram chamadas) |
| Snippets | Parsing da sintaxe LSP, placeholders tipados, UUID conforme modo de identificador |
| Faixa de substituição | Todas as linhas da tabela de [faixas e aspas](traditional-autocomplete.md#faixas-e-aspas) |
| Incompleto | `IsIncomplete` e reconsulta ao `Changed` com mesma versão; sem reconsulta com versão diferente |
| Cancelamento | Nova tecla fora do token cancela; resultado de versão antiga descartado |
| Resolve | Documentação só do item destacado; cancelada ao mudar seleção |

## Editor (Headless)

- `Ctrl+.` e `Ctrl+Espaço` abrem a lista; `↑`/`↓` navegam; `Tab`/`Enter` aceitam; `Esc` fecha.
- Linhas da [tabela de arbitragem](editor-integration.md#arbitragem-de-teclado), uma por teste.
- Snippet: `Tab`/`Shift+Tab` entre placeholders, placeholders espelhados, `$0`, desfazer em uma unidade.
- Atalhos lidos de `EditorKeyBindings`; preferência inválida não sobrescreve sessão.
- Casamento por `KeySymbol` simulado para `;` e `.`.
- Ghost nativo não altera texto, undo, seleção ou cópia; acompanha rolagem e quebra.
- PNGs nos 18 cenários (tema × tamanho × escala) para lista, snippet ativo, indicador de IA e ghost multilinha.

## Concorrência

- `TimeProvider` falso (já injetável em `AutocompleteService`) para debounce, TTL, backoff e timeouts determinísticos.
- Cenário **Request A → digitação → Request B**: A cancelada; se A terminar depois de B (provider que ignora cancelamento), a UI mostra somente B.
- Carga do modelo não é cancelada por digitação (teste existente mantido).
- `Ctrl+;` preempta inline; chat preempta inline; resultado preemptado descartado.
- Abas distintas nunca cancelam uma à outra.

## IA

| Aspecto | Testes |
| --- | --- |
| Construção de contexto | Prompt-ouro por contrato e fixture; `editor-context-v1` idêntico ao atual |
| Seleção | Campos de outras coleções nunca incluídos; filhos do caminho parcial priorizados; statement semelhante inteiro ou ausente |
| Orçamento | Propriedade: nunca excede `ContextTokens`; redistribuição de sobras |
| Tokenização | Estimativa dentro da margem; cache de blocos invalidado ao trocar modelo |
| Modelos diferentes | Adapters Qwen e DeepSeek falsos; contrato não suportado rejeitado |
| Tamanhos de contexto | 256, 512, 1024, 2048 |
| Fallback | Cada linha da [matriz de fallback](ai-autocomplete.md#fallback) |
| Cancelamento e timeout | Geração interrompida; prévia parcial válida preservada ou lista aberta |
| Streaming | Texto final igual ao modo não streaming |
| Output Processor | Eco de sufixo, parada estrutural, estilo de aspas, identificador desconhecido, privacidade |
| Prefix cache | Com fake: sequência de `RewindTo`/`AppendTokens` correta e invalidação |

## IA com modelos reais

Testes `Explicit`, categoria `LocalModelIntegration`, seguindo o padrão existente:

| Variável | Uso |
| --- | --- |
| `SLOP_QWEN_MODEL` | Pasta de um modelo qwen2 (inclusive SlopCoder-Mongo 1.5B e pacotes DML) |
| `SLOP_DEEPSEEK_MODEL` | Pacote DeepSeek FIM |
| `SLOP_TEST_GPU=1` | Exige provider GPU; fallback CPU não conta como aprovação |

Cobertura: CPU; GPU quando disponível; NPU quando houver pacote e hardware; equivalência greedy com e sem prefix cache; harness de avaliação de contratos; relatório JSON de latência em `TestResults`. O nome do TRX registra modelo e hardware, como em [23](../23-onnx-slopcoder.md).

## Preemptivo

- Rajada de 20 teclas abaixo do debounce → nenhuma geração; após a pausa → no máximo uma.
- Movimento de cursor sem edição → nenhuma requisição.
- Typeahead compatível encolhe o ghost sem nova requisição; incompatível invalida.
- Inline nunca inicia carga de modelo.
- Camada 1 desativada quando o perfil de latência excede o orçamento.
- Comportamentos atuais preservados: sufixo não duplicado, Tab incremental, Esc, rejeição de resposta obsoleta.

## Métricas

`MeterListener` coleta todas as medições de um cenário completo e verifica que cada tag pertence à lista permitida e que nenhum valor contém texto da fixture (nomes de campos, coleções, trechos do editor).

## Benchmarks

Descritos em [performance.md](performance.md#benchmarks). Resultados de aceite são anexados à PR da fase, com máquina e build.

## Homologação manual

| Item | Evidência |
| --- | --- |
| `Ctrl+.` e `Ctrl+;` em Windows US e ABNT2, Linux X11 e Wayland | Registro na matriz |
| IME (composição não dispara lista/ghost) | Registro |
| Leitor de tela na lista | Registro |
| MongoDB real com permissões restritas, views e time series | Registro |
| Modelos reais em CPU e GPU com uso interativo | Registro com latência observada |
