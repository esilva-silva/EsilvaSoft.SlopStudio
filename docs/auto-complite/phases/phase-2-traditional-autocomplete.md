# Fase 2 — Autocomplete tradicional reformulado

Roadmap: v0.6.0 (EDT-02) · Depende de: Fase 1 · Habilita: Fase 3 e 5.1 (tradicional preemptivo, sem dependência IA)

## Objetivo

Substituir o antigo menu `Ctrl+Espaço` por uma lista contextual baseada em documento → parser → `CompletionContext` → provider → catálogo → ranking → itens → editor, com baixa latência, UI sempre responsiva, cancelamento rápido, snippets com placeholders e atalhos como dados. Desde o lote W0 (18/09/2026), `Ctrl+Espaço` também é o gatilho padrão da lista nova — `Ctrl+.` saiu dos padrões por decisão do produto ([AC-08](../decisions.md#ac-08--atalhos)).

## Situação inicial da meta (histórica)

- `ShowSuggestions` monta `MenuFlyout` concatenando `MqlAutocompleteService`, `ConsoleAutocompleteService` e uma sugestão IA/básica; sem ranking, filtro, tipos ou placeholders; sugestões MQL substituem 0..cursor.
- Seis mecanismos independentes de interpretação do texto.
- Ghost text atual executa trabalho pesado na UI por tecla e por movimento de cursor.
- Atalhos codificados; `AvaloniaEdit` completion e snippets não utilizados.

O estado verificado desta implementação está na tabela **Estado da implementação** abaixo; os itens acima preservam o ponto de partida da meta e não descrevem o checkout atual.

## Incrementos

| # | Entrega | Resultado verificável |
| --- | --- | --- |
| 2.1 | `AvaloniaTextSnapshot`; extração do `MongoLexer` do highlighting | Highlighting inalterado; snapshot sem cópia |
| 2.2 | `TolerantParser`, `SyntaxTreeCache`, reparse por statement | Testes de propriedade e diferencial |
| 2.3 | Inicial: contexto lexical, alvo estático e `ShapeWalker` de parâmetros; faltam memoização, escopos JS completos e integração com o editor | Fixtures de contexto |
| 2.4 | `CompletionService`, `CompletionRanker`, `SnippetTemplate`, resolve tardio, `CompletionUsageTracker` | Conjunto-ouro de ranking |
| 2.5 | Desktop: `CompletionWindowPresenter`, `SnippetInserter`, `EditorCommandDispatcher`, `EditorKeyBindings`, arbitragem | Headless + PNG |
| 2.6 | Migração: remover `MenuFlyout`; tornar obsoletos serviços antigos; ghost atual obtém campos pelo catálogo e contexto (tira trabalho da UI) | Sem chamadores de produção dos serviços antigos |

## Alterações

| Projeto | Arquivo | Alteração |
| --- | --- | --- |
| Core | `WorkspaceSession.cs` | `EditorKeyBindings` aditivo |
| Core | `Autocomplete.cs` | `CompletionAutoOpenOnTrigger`, `CompletionEnterAccepts` (aditivos) |
| Application | `SyntaxHighlighting/SyntaxHighlightingService.cs` | Consome tokens do `MongoLexer` |
| Application | `MongoCompletionTarget.cs` | Substituído por `NamespaceTargetResolver`; casos de teste migrados |
| Application | `AggregationCompletionContext.cs`, `ConsoleAutocompleteService.cs` | Removidos após migração |
| Application | `MqlAutocompleteService.cs` | Sugestões removidas junto da lista de operadores duplicada; inferência de schema preservada |
| Application | `CompletionSession.cs` | Generalizado em `EditorRequestScope` |
| Desktop | `SyntaxHighlighting/MongoTextEditor.cs` | Snapshot, hooks para janela de completion |
| Desktop | `WorkspaceTabView.axaml.cs` | `EditorKeyDown` → dispatcher; `ShowSuggestions` removido |
| Desktop | `WorkspaceTabView.Autocomplete.cs` | Captura por snapshot; campos via catálogo |
| Desktop | `ViewModels/WorkspaceTabViewModel.cs`, `.Autocomplete.cs` | `GetConsoleCompletionsAsync` e `GetObservedCompletionFields` substituídos |
| Desktop | `MainWindow.axaml.cs` | `OnWorkspaceKeyDown` consulta o dispatcher |
| Desktop | `App.axaml` | Estilos da lista nos temas Light/Dark |
| Docs | [06](../../06-editor-bson-e-uuid.md), [17](../../17-design-system-ui-ux.md), [21](../../21-autocomplete-local.md), [22](../../22-syntax-highlighting.md) | Pipeline, atalhos e arquitetura atualizados |

## Novos componentes

`ITextSnapshot`, `AvaloniaTextSnapshot`, `MongoLexer`, `MongoSyntaxTree`, `TolerantParser`, `SyntaxTreeCache`, `CompletionContextEngine`, `CompletionContext`, `NamespaceTargetResolver`, `ShapeWalker`, `PipelineInfo`, `CompletionService`, `CompletionItem`, `CompletionList`, `CompletionRanker`, `RankingProfile`, `SnippetTemplate`, `CompletionUsageTracker`, `EditorRequestScope`, `CompletionWindowPresenter`, `SnippetInserter`, `EditorCommandDispatcher`, `EditorKeyBindings`, `LanguageServiceOptions`.

## Fluxo

[README — Fluxo principal](../README.md#fluxo-principal-ctrl) e [traditional-autocomplete.md](../traditional-autocomplete.md#fluxo).

## Dependências

- Fase 1: catálogo, cache, schema, métricas e benchmarks.
- Nenhuma dependência de IA.

## Performance

- UI thread: somente captura (snapshot O(1)) e aplicação.
- Contexto memoizado por versão e cursor; estreitamento sem reparse enquanto a lista está aberta.
- Consulta ao catálogo limitada aos tipos esperados; top-K parcial.
- Ghost atual deixa de re-lexar prefixo e inferir campos na UI.
- Medir: construção de contexto por tamanho de documento e posição de edição; tecla → lista visível; refiltro; alocação por tecla; tempo na UI por tecla no ghost atual antes/depois.

## Testes

- Fixtures de contexto de todas as categorias de [testing.md](../testing.md#fixtures-de-contexto), inclusive os casos obrigatórios de [06](../../06-editor-bson-e-uuid.md#casos-obrigatórios) e os exemplos da meta.
- Parser: truncamentos sem exceção; incremental equivalente ao completo.
- Ranking: conjunto-ouro (MRR/top-1/top-5) registrado como baseline; invariantes.
- Provider: prefixos, fontes consultadas, faixas e aspas, snippets tipados, `IsIncomplete`, cancelamento, resolve.
- Headless: abertura, navegação, aceitação, arbitragem, snippets, atalhos de preferências, PNGs.
- Concorrência: Request A/B; provider que ignora cancelamento; abas isoladas.
- Regressão: `AutocompleteUiTests`, `PredictiveAutocompleteTests`, `MongoCompletionTargetTests` (migrados), `SyntaxHighlighting*`.

## Critérios de aceite

1. Todas as fixtures de contexto aprovadas, cobrindo 100% dos casos obrigatórios de 06 e dos exemplos da meta.
2. Testes de propriedade (truncamentos) e diferencial (incremental × completo) aprovados com as sementes registradas.
3. Nenhum item inválido para o shape aparece no top-5 em todo o conjunto-ouro; MRR/top-1/top-5 registrados como gate de não regressão.
4. `Ctrl+Espaço` abre a lista; `↑`, `↓`, `Tab`, `Enter` e `Esc` seguem os [comandos por escopo](../editor-integration.md#arbitragem-de-teclado), um teste por linha. `Ctrl+.` não é mais padrão, mas um override salvo continua funcionando (W0, 18/09/2026).
5. Snippets navegáveis com `Tab`/`Shift+Tab`; inserção desfeita em uma única operação.
6. Todas as linhas da [tabela de faixas e aspas](../traditional-autocomplete.md#faixas-e-aspas) verificadas, inclusive `"Cliente.Id"` a partir de `Cliente.`.
7. Resultado de versão antiga nunca é aplicado (teste com provider lento que ignora cancelamento).
8. Digitação não gera chamada remota; cargas de metadados seguem exclusivamente as regras do cache.
9. Orçamentos de UI por tecla, construção de contexto e tecla → lista (revisados após a Fase 1) atendidos na máquina de referência para documentos até 64 KiB, ou desvio justificado e aprovado.
10. PNGs dos 18 cenários inspecionados para lista aberta, detalhe e snippet ativo nos dois temas.
11. `MenuFlyout` removido; `ConsoleAutocompleteService` e `AggregationCompletionContext` removidos somente se não houver consumidores legados; sugestões de `MqlAutocompleteService` removidas por não terem chamadores de produção.
12. Atalhos lidos de `EditorKeyBindings`; preferência inválida não sobrescreve a sessão.
13. Testes existentes aprovados; adaptações de asserção justificadas na PR sem enfraquecer o comportamento verificado.
14. Documentação 06, 17, 21, 22, 24 e matriz atualizadas; decisões AC-01, AC-02, AC-07, AC-08, AC-09 e AC-17 promovidas ou revisadas.

## Riscos

| Risco | Mitigação |
| --- | --- |
| Gramática crescer além do necessário | Subconjunto fixo; `OpaqueStatement`; casos só por necessidade real |
| Regressão do highlighting ao extrair o lexer | Testes inalterados; PNGs comparados |
| `CompletionWindow` difícil de estilizar ou virtualizar | Protótipo no 2.5 com 100 itens; alternativa de controle próprio documentada |
| Conflitos de `Tab` com ghost e snippet | Tabela de arbitragem testada linha a linha |
| `Ctrl+;`/`Ctrl+Espaço` em layouts e IMEs | Casamento por espécie do gesto (tecla nomeada vs. pontuação); `Ctrl+Espaço` colide com troca de IME em Windows/Linux — limitação registrada, sem tela de edição de atalhos; homologação manual pendente |
| Documentos grandes | Limites de análise por statement; benchmark de 1 MB |
| Schema incompleto gerando confiança excessiva | Evidência no detalhe; penalidade de tipo sem remoção |
| Migração quebrar fluxos do Console | Fixtures do Console, testes de integração existentes |

## Fora do escopo

IA explícita, mudanças no preemptivo além de mover trabalho da UI para o catálogo, persistência de uso, tela de edição de atalhos.


## Estado da implementação

Execução iniciada em 15/09/2026 com os perfis de [agents](../agents/README.md). Validação somente em Windows x64 (AMD Ryzen 9 7900, Windows 11 25H2, .NET 10.0.12); Linux e ARM fora do critério desta fase.

| Incremento | Estado | Evidência |
| --- | --- | --- |
| Pré-requisito K12 (MongoDB Knowledge) | Concluído | Geração por entrada no `MetadataCache`: write-through, invalidação soft, disconnect e opt-out descartam carga/amostra tardias; `Changed` terminal com `Key` para sucesso, falha, cancelamento e descarte; `CatalogQuery.Access` propagado a todos os `Get` (Peek não agenda carga; escopo sem valor e sem carga = `Unavailable`). Os 3 `PhaseOneReviewTests` vermelhos da Fase 1 passam sem mudança de asserção; 14 testes novos |
| Pré-requisito T08 Core (Traditional Completion) | Concluído (Core/persistência e despacho inicial) | `AutocompleteSettings.CompletionAutoOpenOnTrigger=false`, `CompletionEnterAccepts=true`; `WorkspacePreferences.EditorKeyBindings` v1 aditivo com `EditorKeyGesture` (tecla nomeada ou pontuação, sem Avalonia); ausente → padrões não gravados; inválido, repetido ou em conflito → sessão ilegível, falha visível e nunca sobrescrita (leitura, gravação e autosave). `EditorCommandDispatcher` recebe símbolo produzido antes do fallback físico, e a aba recebe os gestos efetivos do workspace. Testes Core e Headless provam ABNT2 simulado, fallback sem símbolo e que uma preferência substitui `Ctrl+.`. |
| 2.1 Snapshot e lexer compartilhado | Application/Desktop concluída; baseline de UI pendente | `AvaloniaTextSnapshot` usa `TextDocument.CreateSnapshot()` sem materializar o texto, mantém offsets UTF-16, histórico via `ITextSourceVersion` e foi integrado ao pedido tradicional do editor; `Language.Text` (`TextSpan`, `ITextSnapshot`, `TextSnapshotVersion`, `StringTextSnapshot`) e `Language.Syntax.MongoLexer` único continuam consumidos pelo highlighting. Golden de 235 casos gerado **antes** da extração e idêntico depois; `SyntaxHighlighting*` sem alteração; PNGs `syntax-*` idênticos na região do editor. Job curto (não é aceite): highlight 16 KiB 471,7 → 113,3 µs; 64 KiB 3 631,6 → 957,4 µs; `MongoLexer.Tokenize` 1 MB 2,6 ms sem alocação. Medição UI por tecla ainda pendente |
| 2.2 Parser tolerante incremental | Em andamento — tokens no caminho ativo, árvore sob demanda | `MongoSyntaxTree`/`TolerantParser` adicionados; truncamentos, fechamento incompatível/ausente, profundidade 512 e semicolon em grupos cobertos. `SyntaxTreeCache` reutiliza statements não afetados em edição única, preserva equivalência diferencial e descarta resultados tardios; históricos com múltiplas mudanças fazem fallback seguro para parse completo. Lote W2a (18/09/2026): o contexto passou a obter tokens pela árvore e o `NamespaceTargetResolver` ganhou sobrecarga por tokens — fim da segunda lexificação por análise. Lote W2a2 (18/09/2026): como o contexto lê tokens e ninguém lê nós, o caminho por tecla passou ao novo `TokenCache` (texto + tokens da última versão por documento) e **a árvore deixou de ser construída ao digitar**; `SyntaxTreeCache` fica intacto e testado para C22/C25. Equivalência diferencial motor-com-cache × motor-sem-cache provada nas 4 sementes (25, 1183, 7341, 20260915) × 40 edições × 8 carets, além dos testes de aba/liberação/cancelamento do `TokenCache`. Benchmarks `CompletionContextBenchmarks` (ShortRun, Ryzen 9 7900, Windows 11 25H2, .NET 10.0.12), por tecla: 1 KiB/fim 20,9 → 13,5 µs; 16 KiB/fim 371 → 196 µs; 64 KiB/fim 4,81 ms → 0,66 ms; 1 MiB/meio 53,0 → 7,38 ms; 1 MiB/início 49,4 → 2,90 ms, contra a linha de base por tecla sem cache de 13,4 µs / 197 µs / 0,64 ms / 7,78 ms / 3,69 ms (0,84–1,05×). Mesma versão (refiltro) continua 0,14–0,78× da análise sem cache (1 MiB/início: 3,29 ms → 71 ns) e o alvo por tokens ~0,57× do alvo por texto. Pendentes: reparse realmente parcial/green tree, checkpoints lexicais e janela por statement acima de 64 KiB |
| 2.3 | Inicial | Papéis lexicais, spans, alvo estático, aliases `const` e shapes de parâmetros cobertos; o escopo agora propaga a coleção explicitamente resolvida (`db.Pedidos`) em vez da coleção padrão da aba; faltam memoização, escopos JS completos e integração com o editor |
| 2.4 | Inicial | `CompletionContextCache` memoiza por identidade completa do snapshot, cursor, dialeto, escopo e gatilho, e a aba reutiliza sua versão enquanto o texto não muda. Uma lista visível é reconsultada em `Peek` quando `MetadataCache.Changed` afeta o perfil/banco/coleção da aba, preservando `SymbolId`; digitação não agenda carga. `PipelineInfo` conservador cobre estágios preservadores, projeção, grupo, lookup e count; falta ligá-lo ao parser/contexto real e cobrir facets/unwind/replaceRoot |
| 2.4 Completion/ranking | Núcleo inicial | Contratos de contexto/item/edição, `CompletionService`, provider tradicional, ranking determinístico, snippets LSP e uso em memória adicionados; faltam shapes semânticos, quotas, top-K parcial, resolve tardio e corpus MRR |
| 2.4 | Inicial | Ranker usa top-K parcial determinístico, preserva destaques e não deduz tipo por texto localizado; ainda faltam gates de ranking/uso integrado |
| 2.5 | Inicial | A lista contextual sobreposta usa `CompletionWindowPresenter`, seleção por `SymbolId`, filtro por texto, ↑/↓/Enter/Tab/Esc, snippets navegáveis e `VirtualizingStackPanel`; um teste cobre 100 itens ordenados. `Ctrl+.`/`Ctrl+Espaço` e a preferência persistida são despachados por símbolo produzido e fallback físico; `Enter` respeita `CompletionEnterAccepts`. A documentação estruturada e livre de valores é resolvida em worker só para o item destacado, com cancelamento e descarte por geração; PNGs `traditional-list-Light/Dark-960` foram inspecionados. Faltam acessibilidade aprofundada e matriz visual completa |
| 2.6 | Inicial | `MenuFlyout`, `ConsoleAutocompleteService`, `ShowSuggestions` e `AggregationCompletionContext` saíram do caminho da lista. As APIs de sugestão MQL estão obsoletas e sem chamadores de produção; a inferência de schema permanece. Falta migrar o ghost para contexto/catálogo e medir a UI por tecla antes/depois |
| W0 — Política de atalhos do autocomplete | Concluído e validado | `EditorCommandScope { Global, List, Snippet, Inline }` e doze `EditorCommandIds` substituem a arbitragem descritiva por resolução por escopo (`EditorCommandDispatcher.Match(keyEvent, scope)`); `Ctrl+.` saiu dos padrões, `Ctrl+Espaço` é o único gatilho padrão do básico, `Ctrl+;` é reconhecido sem runtime (nunca insere `;` nem abre a lista tradicional). Um override de `Ctrl+.` já salvo por um usuário continua legível e não é reescrito. Corrigidos três defeitos: `Ctrl` sozinho abrindo a lista e tecla desconhecida aceitando o ghost (casamento por espécie do gesto substitui `||` entre `Key`/`Symbol`), gestos de `Enter` físico nunca casando, e navegação `↑`/`↓` sendo um no-op por reemissão de `SelectionChanged` ao trocar `ItemsSource`. Textos de UI passaram a derivar dos gestos efetivos com projeção em pt-BR (`↑`, `↓`, `Esc`, `Ctrl+Espaço`). Build 0 avisos; suíte completa **1170 aprovados, 0 falhas**. Limitação registrada e não resolvida: `Ctrl+Espaço` colide com troca de IME em Windows/Linux, sem tela de edição de atalhos para contornar. Homologação em layouts físicos reais (ABNT2/US em Windows; X11/Wayland em Linux), IME real e leitor de tela seguem pendentes — teste Headless não as substitui. Detalhe em [editor-integration.md](../editor-integration.md#arbitragem-de-teclado) e [AC-08](../decisions.md#ac-08--atalhos) |

Suíte após integração dos lotes acima, incluindo W0: **1170 testes aprovados, 0 falhas**, build 0 avisos.

## Revisão de tarefas e pré-requisitos

[C21–C25 e T01–T08](../execution-plan.md) refinam os incrementos acima. Parser de statement primeiro, incrementalidade depois; manter AggregationFieldInference/facet/count até paridade. Presenter compartilhado T07 é prototipado aqui para atender Fase 4 e 5.1 sem ciclo de dependência. Ghost atual pode continuar até integração aprovada; nunca remover serviços ainda usados por ele.

Completar K11–K17/L11–L16 para aceite da base de dados, mas contexto pode ser desenvolvido com snapshots falsos em paralelo após G00. Flags/atalhos conforme configuration.md; evidências de sesión, UI e schema ficam separadas. Arquivos/propostas de nomes acima são mapeados aos reais, sem renomear CatalogModel só para satisfazer desenho.
