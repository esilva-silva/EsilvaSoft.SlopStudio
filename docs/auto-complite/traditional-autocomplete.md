# Autocomplete tradicional

## Objetivo

Lista contextual explícita (`Ctrl+Espaço`), totalmente determinística e funcional sem IA, com ranking, filtro enquanto se digita, tipos, snippets com placeholders, documentação tardia e faixa de substituição exata. Desde o lote W0 (18/09/2026), `Ctrl+.` saiu dos padrões de teclado — ver [política de atalhos](editor-integration.md#atalhos) e [AC-08](decisions.md#ac-08--atalhos); um override explícito já salvo por um usuário continua funcionando.

## Fluxo

```text
Snapshot do documento (O(1))
       ↓
Lexer compartilhado → Parser tolerante (reuso por versão)
       ↓
CompletionContext (papel, esperado, alvo, escopos, faixa)
       ↓
TraditionalCompletionProvider → CompletionService: fontes pelo esperado
       ↓
Knowledge Catalog (somente escopos necessários; sem I/O)
       ↓
Ranking (filtros rígidos → pontuação → top-K)
       ↓
CompletionList (versão, IsIncomplete)
       ↓
CompletionWindow (AvaloniaEdit) + snippets
```

## Seleção de fontes

O provider consulta apenas os grupos relevantes:

| Esperado | Consulta | Não consulta |
| --- | --- | --- |
| `Field` | Schema da coleção alvo (validator, índices, resultados, amostra) + uso | Conexões, bancos, métodos, stages |
| `QueryOperator`, `UpdateOperator`, `ExpressionOperator`, `Accumulator` | Linguagem, filtrada pelo shape e tipada pelo campo pai | Metadados remotos |
| `AggregationStage` | Linguagem + snippets de stage | Schema |
| `Collection` | Metadata Cache do banco alvo | Schema, índices |
| `Database` | Metadata Cache da conexão alvo | Coleções |
| `Connection` | Perfis | Metadados |
| `*Method` | Linguagem do dialeto para o receptor | Metadados |
| `Index` | Índices da coleção alvo | Schema |
| `SearchOperator`/`SearchOption` | Linguagem (Atlas/MongoDB Search) | — |
| `EnvironmentKey` | Nomes das chaves do ambiente ativo | Valores |
| `LocalVariable`, `Keyword`, `DslRoot` | Árvore do editor, linguagem | Metadados |

Se algum escopo exigido estiver `Loading` ou `Stale`, a lista é devolvida com `IsIncomplete = true` e uma linha de estado discreta ("Carregando coleções de Projetos…"). Quando o cache publica `Changed` para aquele escopo e a versão do documento ainda é a mesma, a lista aberta é reconsultada, preservando o item selecionado por `SymbolId`.

## Itens

```csharp
public sealed record CompletionItem(
    SymbolId? Symbol,
    string Label,
    string? LabelDetail,              // "binData · UUID", "string · 98%", "Cursor"
    CompletionItemKind Kind,
    CompletionEdit Edit,              // texto ou snippet + faixas insert/replace
    string FilterText,
    double Score,
    CompletionSource Source)          // Catalog, Schema(evidência), Snippet, Local, Ai
{
    public IReadOnlyList<TextSpan> Highlights { get; init; } = [];
    public CompletionItemTags Tags { get; init; }        // Deprecated, Write, Stale, Ai
    public IReadOnlyList<char> CommitCharacters { get; init; } = [];
}

public sealed record CompletionList(TextSnapshotVersion Version, IReadOnlyList<CompletionItem> Items, bool IsIncomplete);
```

Documentação, exemplos, assinatura completa e link oficial vêm de `ResolveAsync` apenas para o item destacado, com cancelamento ao mudar a seleção.

## Faixas e aspas

| Situação | Texto inserido | Faixa |
| --- | --- | --- |
| Coleção identificadora após `db.` | `Clientes` | Token parcial |
| Coleção com caractere inválido após `db.` | `["pedidos-2025"]` | Inclui o `.` e o parcial (comportamento atual preservado) |
| Campo simples em chave | `Nome` ou `"Nome"`, conforme o estilo dominante do objeto | Token parcial |
| Caminho com ponto em chave | `"Cliente.Id"` sempre com aspas | Inclui aspas abertas e o trecho `Cliente.` digitado |
| Dentro de string já aberta | Apenas o conteúdo; fecha a aspa só se não houver fechamento adiante | Conteúdo parcial |
| Operador em chave | `$eq` ou `"$eq"`, conforme estilo | Token parcial, inclusive `$` |
| Referência de campo em expressão | `"$Cliente.Id"` | Conteúdo da string |
| Método sem `(` à frente | `find` + snippet `({ $1 })` | Token parcial |
| Método com `(` à frente | Apenas o nome | Token parcial |

O estilo dominante é calculado pelos tokens do objeto e do statement (chaves com ou sem aspas, aspas simples ou duplas). A inserção passa por `Document.RunUpdate`, formando uma única unidade de desfazer.

## Refinamento enquanto se digita

Com a lista aberta, caracteres de identificador refinam a lista reutilizando o contexto estreitado (sem reparse). Caracteres que mudam o papel (`:`, `,`, `{`, `}`, `(`, `)`, espaço fora de string) fecham a lista. `Backspace` além do início do token fecha. `CommitCharacters` começam vazios; candidatos (`.` após coleção, `:` após campo) serão avaliados pela taxa de aceite e reversão.

## Snippets

Armazenados na sintaxe LSP (`$1`, `${1:placeholder}`, `${1|a,b|}`, `$0`, `\$` para cifrão literal) e convertidos em elementos do AvaloniaEdit ([editor-integration.md](editor-integration.md#snippets)). `Tab` e `Shift+Tab` navegam; `Enter` ou `Esc` encerram a sessão de snippet.

Placeholders tipados: quando o campo tem tipo conhecido, o valor sugerido usa o construtor correspondente. Para UUID e ObjectId, o texto de exemplo vem de `IdentifierRepresentationService.ScriptIdentifierPlaceholder`, respeitando o modo de identificador e a representação UUID da conexão.

Conjunto inicial (Fase 2):

```text
filter.equals        ${1:campo}: ${2:valor}
filter.condition     ${1:campo}: { \$eq: ${2:valor} }
filter.in            ${1:campo}: { \$in: [${2}] }
filter.range         ${1:campo}: { \$gte: ${2}, \$lt: ${3} }
filter.or            \$or: [{ ${1} }, { ${2} }]
stage.match          { \$match: { ${1} } }
stage.project        { \$project: { ${1:campo}: 1 } }
stage.group          { \$group: { _id: "\$${1:campo}", ${2:total}: { \$sum: ${3:1} } } }
stage.lookup         { \$lookup: { from: "${1:colecao}", localField: "${2}", foreignField: "${3}", as: "${4:resultado}" } }
stage.unwind         { \$unwind: "\$${1:campo}" }
update.set           { \$set: { ${1:campo}: ${2:valor} } }
method.updateOne     updateOne({ _id: ${1:id} }, { \$set: { ${2} } })
method.createIndex   createIndex({ ${1:campo}: ${2:1} }, { name: "${3}" })
```

A condição da meta (`{ Campo: { $eq: ${1:value} } }`) vira `filter.condition`, aplicado ao campo sob o cursor; para um campo UUID, o valor é `UUID("…")`. Snippets de escrita recebem a marca `Write`; inserir nunca executa, e a confirmação do runtime continua obrigatória.

## Estados da lista

| Estado | Apresentação |
| --- | --- |
| Carregando escopo | Itens disponíveis + linha "Carregando…" |
| Stale | Itens + indicação discreta no detalhe |
| Sem itens, catálogo completo | Lista não abre; mensagem curta na barra de status |
| Campos desconhecidos (sem schema) | Linha de ação "Amostrar schema (nomes e tipos, 100 documentos)" |
| Conexão desconectada | Somente linguagem + cache existente; linha "Conecte para carregar metadados" |
| Falha de metadado | Itens disponíveis; mensagem uma vez por escopo |

## Limites

| Limite | Valor inicial |
| --- | --- |
| Candidatos ranqueados | 200 |
| Itens exibidos | 100 |
| Tempo de espera por contexto antes de abrir com o disponível | 50 ms (provisório) |

## Configurações (aditivas em `AutocompleteSettings` v1)

| Campo | Padrão | Efeito |
| --- | --- | --- |
| `CompletionAutoOpenOnTrigger` | `false` | Abrir a lista ao digitar `.` e `$` |
| `CompletionEnterAccepts` | `true` | Enter aceita com a lista aberta |
| `WorkspacePreferences.SchemaSamplingProfileIds` (existente) | vazio | Opt-in por conexão; não duplicar preferência |

## Migração

| Hoje | Depois |
| --- | --- |
| `ShowSuggestions` + `MenuFlyout` | `CompletionWindowPresenter` |
| `MqlAutocompleteService.GetSuggestions`/`GetAggregationSuggestions`/`ApplySuggestion` | `CompletionService` (removidos; o catálogo é a única fonte de operadores) |
| `ConsoleAutocompleteService` | Fontes de coleções/métodos + resolvedor de alvo |
| Sugestão IA/básica no topo do menu | Removida da lista; IA passa ao `Ctrl+;` |
| Operação visível "Gerando sugestões locais" | Removida (trabalho em memória); só cargas remotas aparecem com prioridade `Low` |


## Revisão de 15/09/2026

Lista e preemptivo tradicionais usam o mesmo CompletionService/ranker/snippets/contexto. Só a lista permite correções antes do cursor, escolhas de placeholder e fuzzy limitado. Providers não mantêm catálogos privados.

No automático/refiltro, Query usa Peek. Na invocação explícita, solicitar refresh limitado do escopo necessário e retornar itens atuais; Changed terminal reconsulta só se stamp válido, preservando seleção. Complete não garante schema exaustivo. Catálogo limitado precisa expor truncamento e refazer busca ao estreitar/alargar prefixo se necessário.

Não remover MongoCompletionTarget, inferência MQL ou Console enquanto ghost/execução ainda os usarem. Adaptar fachadas e remover apenas chamadores migrados, preservando AggregationFieldInferenceTests. [Tarefas T01–T08](execution-plan.md), [configuração](configuration.md).
