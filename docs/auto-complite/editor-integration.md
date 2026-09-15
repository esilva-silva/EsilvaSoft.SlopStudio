# Integração com o editor

Camada Desktop. Converte contratos da Application em recursos nativos do AvaloniaEdit, arbitra teclado e aplica atalhos. Nenhuma regra MongoDB vive aqui.

## Recursos do AvaloniaEdit 12.0.0 utilizados

Presença verificada no assembly do cache NuGet:

| Recurso | Uso |
| --- | --- |
| `TextDocument.CreateSnapshot`, `ITextSourceVersion`, `TextChangeEventArgs` | `AvaloniaTextSnapshot` sem copiar o documento |
| `CompletionWindow`, `CompletionList`, `ICompletionData` (`Priority`, `Complete`) | Lista tradicional |
| `Snippet`, `SnippetReplaceableTextElement`, `SnippetBoundElement`, `SnippetCaretElement`, `InsertionContext`, `SnippetInputHandler` | Placeholders e navegação por Tab |
| `VisualLineElementGenerator`, `FormattedTextElement`, `InlineObjectElement` | Ghost text no layout do editor |
| `IBackgroundRenderer`, `KnownLayer` | Indicador de geração em andamento |
| `TextAnchor` | Posição da sugestão estável durante typeahead |
| `Document.RunUpdate`, `UndoStack` | Inserção como uma unidade de desfazer |

## Snapshot

`AvaloniaTextSnapshot : ITextSnapshot` encapsula `ITextSource` e `ITextSourceVersion`. A propriedade `Text` ligada ao view model continua existindo para execução, rascunhos e testes, mas o autocomplete deixa de depender dela.

## Lista (`CompletionWindowPresenter`)

- Recebe `CompletionList` e cria `ICompletionData` com rótulo, detalhe, selo de tipo em texto (sem novos ícones), destaque dos intervalos casados e descrição tardia.
- O filtro interno do `CompletionList` fica desativado; a cada tecla o presenter pede ao `CompletionService` o refinamento do contexto estreitado e substitui os itens preservando a seleção por `SymbolId`. Assim, a ordem da tela é sempre a do [ranking](ranking.md).
- `StartOffset`/`EndOffset` da janela seguem `ReplaceSpan`; `Complete` aplica `CompletionEdit` (texto ou snippet).
- Estilo por recursos semânticos existentes de `App.axaml` e [17 — Design system](../17-design-system-ui-ux.md); verificação de contraste nos dois temas.
- Acessibilidade: `AutomationProperties.Name` com rótulo, tipo e detalhe; anúncio do total de itens. Leitor de tela nativo não é homologado por Headless.
- Risco a validar: virtualização e desempenho do `ListBox` interno com 100 itens e trocas frequentes.

## Snippets

`SnippetInserter` converte `SnippetTemplate` em `Snippet` do AvaloniaEdit:

| Sintaxe | Elemento |
| --- | --- |
| Texto | `SnippetTextElement` |
| `${n:placeholder}` (primeira ocorrência) | `SnippetReplaceableTextElement` |
| `$n` repetido | `SnippetBoundElement` ligado ao anterior |
| `$0` | `SnippetCaretElement` |
| `${n\|a,b\|}` | Placeholder com o primeiro valor + lista tradicional de escolhas ao entrar no placeholder |

A inserção ocorre dentro de `Document.RunUpdate`. Ao encerrar a sessão de snippet, o ghost e a lista são recalculados normalmente.

## Ghost text

Substitui a sobreposição `InlineCompletionTextBlock` + `Canvas` ([preemptive-autocomplete.md](preemptive-autocomplete.md#renderização)):

- **Primeira linha:** `GhostTextElementGenerator` insere um `FormattedTextElement` na posição do `TextAnchor` da sugestão, com brush `Syntax.GhostText`; o texto real após o cursor é deslocado pelo próprio layout, sem redesenho do sufixo.
- **Linhas seguintes:** um `InlineObjectElement` no fim da linha hospeda um bloco de texto multilinha (altura extra da linha visual). Alternativa se houver problema de caret/hit-testing: renderizador em camada de fundo.
- O documento nunca é alterado pela prévia; seleção, busca, cópia e undo ignoram o ghost.
- Limite inicial de linhas exibidas: 8 (provisório).

## Arbitragem de teclado

Prioridade de estados: **lista aberta › sessão de snippet › ghost visível › IA pendente › nenhum**.

| Tecla | Lista aberta | Snippet ativo | Ghost visível | IA pendente | Nenhum |
| --- | --- | --- | --- | --- | --- |
| `Tab` | Aceita item | Próximo placeholder | Aceita (inteiro ou incremental) | Comportamento normal | Normal |
| `Shift+Tab` | — | Placeholder anterior | Normal | Normal | Normal |
| `Enter` | Aceita item (configurável) | Encerra snippet e insere linha | Nova linha, descarta ghost | Nova linha, cancela | Normal |
| `Esc` | Fecha lista | Encerra snippet | Descarta ghost | Cancela geração | Cancela execução em andamento (global, existente) |
| `↑`/`↓` | Navega | Normal | Descarta e move cursor | Cancela e move | Normal |
| `Ctrl+.` | Recalcula | Abre lista | Oculta ghost, abre lista | Cancela IA, abre lista | Abre lista |
| `Ctrl+;` | Fecha lista, pede IA | Pede IA | Substitui ghost por IA | Ignora | Pede IA |
| `Alt+]` / `Alt+[` | — | — | Próxima/anterior alternativa | — | — |
| `Ctrl+→` | Normal | Normal | Aceita próxima palavra (se habilitado) | Normal | Normal |

`F6`, `Ctrl+Enter`, `F5`, `Ctrl+T/W/O/S` e `Ctrl+Tab` permanecem inalterados.

## Atalhos

Não existe sistema de keybindings. É proposto um registro mínimo de comandos, sem tela de edição nesta meta.

| Comando | Padrão |
| --- | --- |
| `editor.completion.show` | `Ctrl+.`, `Ctrl+Espaço` |
| `editor.completion.ai` | `Ctrl+;` |
| `editor.inline.trigger` | `Alt+\` |
| `editor.inline.accept` | `Tab` |
| `editor.inline.acceptWord` | `Ctrl+→` (desligado por padrão) |
| `editor.inline.dismiss` | `Esc` |
| `editor.inline.next` / `editor.inline.previous` | `Alt+]` / `Alt+[` |

- **Persistência:** `EditorKeyBindings { Version = 1, Bindings: comando → gestos }`, aditivo em `WorkspacePreferences`. Ausente → padrões. Validação segue o padrão existente: valor inválido torna a sessão ilegível e ela nunca é sobrescrita.
- **Despacho:** `EditorCommandDispatcher` no handler de túnel do editor substitui os `if` de `EditorKeyDown`; `MainWindow.OnWorkspaceKeyDown` consulta o dispatcher antes das regras globais.
- **Layout de teclado:** teclas nomeadas casam por `Key` + modificadores. Pontuação (`.`, `;`, `]`, `[`, `\`) casa primeiro por `KeyEventArgs.KeySymbol` (caractere produzido pelo layout) e, na ausência, por `PhysicalKey`. Isso evita que `Ctrl+;` dispare pela tecla física de `;` do layout US em teclados ABNT2, onde a posição e o código virtual diferem. Com `Ctrl` pressionado, algumas plataformas podem não entregar `KeySymbol`; validar em Windows (US e ABNT2) e Linux (X11 e Wayland).
- **Conflitos conhecidos:** `Ctrl+.` é usado por alguns IMEs asiáticos para alternar pontuação; documentar no guia. `Ctrl+Espaço` alterna IME em alguns sistemas — por isso `Ctrl+.` passa a ser o principal.
- **Documentação:** tabela de atalhos de [17 — Design system](../17-design-system-ui-ux.md) atualizada na Fase 2.

## Foco, seleção e IME

- Sugestões só com foco no editor e seleção vazia (regra atual).
- Durante composição de IME (texto em pré-edição), ghost e lista automática ficam inibidos.
- Perda de foco fecha lista e ghost, exceto quando o foco vai para a própria janela de completion.
- Troca de aba, modo ou destino invalida tudo (regra atual).

## Temas

Recursos `Syntax.GhostText` (existente), selos de tipo e fundo da lista via recursos semânticos Light/Dark. Evidência PNG nos 18 cenários (tema × tamanho × escala) exigida pelo `AGENTS.md`.
