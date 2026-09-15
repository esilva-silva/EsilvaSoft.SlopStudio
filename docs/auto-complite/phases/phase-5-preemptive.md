# Fase 5 — Autocomplete preemptivo

Roadmap: v0.9.0 · Depende de: Fase 2 (camada 0) e Fase 4 (camada 1)

## Objetivo

Substituir o ghost text atual por um `InlineCompletionCoordinator` que combina Context Engine, catálogo, histórico e IA em camadas, com gatilhos medidos, debounce adaptativo, typeahead sobre a sugestão, renderização nativa no AvaloniaEdit e nenhuma inferência por tecla.

## Situação atual

- `EditorCompletionChanged` reage a texto, cursor e seleção; captura contexto pesado na UI; dicionário imediato; IA após 150 ms fixos.
- Ghost por sobreposição (`InlineCompletionTextBlock`) que redesenha o sufixo.
- Qualquer alteração invalida a sugestão; movimento de cursor dispara inferência.
- Comportamentos corretos a preservar: `Tab` incremental, `Esc`, sufixo não duplicado, resposta obsoleta rejeitada, ghost sem alterar o documento.

## Incrementos

| # | Entrega | Resultado verificável |
| --- | --- | --- |
| 5.1 | Coordinator, gating e escopo de requisição inline | Testes de gating |
| 5.2 | Camada 0: conclusão única, modelos de statement do histórico, `BasicAutocompleteProvider` refatorado | Latência e acerto medidos |
| 5.3 | Camada 1: IA `Background` com orçamento reduzido e política de latência | Testes com perfil falso |
| 5.4 | Typeahead sobre o ghost e restauração por `Backspace` | Contador de requisições |
| 5.5 | Ghost nativo (elemento visual + objeto inline multilinha) | Headless + PNG |
| 5.6 | Instrumentação de gatilhos, debounce adaptativo e experimento | Relatório de gatilhos |
| 5.7 | Migração: remover fluxo e sobreposição antigos; preferências aditivas | Sem código morto |

## Alterações

| Projeto | Arquivo | Alteração |
| --- | --- | --- |
| Core | `Autocomplete.cs` | `InlineEnabled`, `InlineUseAi`, `InlineMaxLines`, `InlineAcceptWord` (aditivos) |
| Application | `CompletionSession.cs` | Substituído por `EditorRequestScope` (já generalizado na Fase 2) |
| Application | `BasicAutocompleteProvider.cs` | Fonte da camada 0 sobre tokens e catálogo, sem regex no documento |
| Application | `AutocompleteService.cs` | `GetImmediateCompletion`/`GetCompletionAsync` do ghost substituídos; fachada de preferências mantida |
| Application | `IncrementalCompletion.cs` | Reutilizado sem mudança |
| Desktop | `WorkspaceTabView.Autocomplete.cs` | Substituído pela integração com o coordinator |
| Desktop | `InlineCompletionTextBlock.cs`, `WorkspaceTabView.axaml` (`CompletionPanel`, `GhostLayer`) | Removidos |
| Desktop | `SyntaxHighlighting/MongoTextEditor.cs` | Registro do `GhostTextElementGenerator` |
| Desktop | `ViewModels/WorkspaceTabViewModel.Autocomplete.cs` | `CaptureAutocompleteRequest` removido |
| Desktop | `AutocompleteSettingsWindow.axaml`, `AutocompleteSettingsViewModel.cs` | Opções inline |
| Docs | [17](../../17-design-system-ui-ux.md), [21](../../21-autocomplete-local.md), [22](../../22-syntax-highlighting.md) | Comportamento e evidências |

## Novos componentes

`InlineCompletionCoordinator`, `InlineGate`, `InlineTriggerPolicy`, `AdaptiveDebounce`, `UniqueCompletionSource`, `StatementTemplateSource`, `InlineSuggestion` (âncora, texto, camada, confiança), `InlineSuggestionCache`, `GhostTextElementGenerator`, `GhostContinuationElement`.

## Fluxo

[preemptive-autocomplete.md — Fluxo](../preemptive-autocomplete.md#fluxo).

## Dependências

- Fase 2: Context Engine, catálogo, ranking, arbitragem de teclado.
- Fase 4: provider de IA, output processor, perfil de latência, prioridades.
- A camada 0 pode ser entregue antes da Fase 4 se o roadmap exigir; a camada 1 não.

## Performance

- Gating e camada 0 em poucos milissegundos, fora da UI.
- Camada 1 somente com modelo carregado, sem disparar carga, e com p95 de TTFT dentro do orçamento.
- Typeahead evita novas requisições durante a digitação que confirma a sugestão.
- Renderização nativa elimina redesenho do sufixo.
- Medir: gatilho → camada 0; pausa → ghost da camada 1; requisições por minuto de digitação; tempo de UI por tecla; CPU média durante digitação contínua com IA habilitada.

## Testes

- Gating: seleção, lista, snippet, IME, comentário/número/regex, meio de identificador, rajada.
- Debounce adaptativo com `TimeProvider` falso.
- Camadas: conclusão única com margem; modelos de histórico só com literais já digitados pelo usuário na mesma conexão; IA estende a camada 0 somente com confiança maior.
- Typeahead e restauração.
- Validação estrita (delimitadores, catálogo, privacidade, sufixo, linhas).
- Concorrência: preempção por `Ctrl+;`/chat; nenhuma carga iniciada pelo inline; resposta obsoleta descartada.
- Headless: ghost nativo de uma e várias linhas, rolagem, quebra, undo, cópia, seleção; arbitragem com lista e snippet; PNGs.
- Adaptação de `PredictiveAutocompleteTests` e `AutocompleteUiTests` preservando os comportamentos listados.
- Métricas `inline.*` com tags permitidas.

## Critérios de aceite

1. Rajada de 20 caracteres com intervalo abaixo do debounce não gera inferência; após a pausa, no máximo uma.
2. Movimento de cursor sem edição não gera requisição.
3. Digitar caracteres iguais ao início do ghost encolhe a sugestão sem nova requisição (contador em teste).
4. O coordinator nunca inicia carga de modelo.
5. O ghost nunca altera texto, histórico de undo, seleção ou conteúdo copiado.
6. Arbitragem com lista aberta e snippet ativo conforme a tabela, um teste por combinação.
7. Sugestão de versão antiga nunca é exibida.
8. Camada 0 dentro do orçamento revisado; camada 1 desativada automaticamente quando o perfil de latência excede o orçamento (teste com perfil falso) e ativa quando dentro.
9. Comportamentos preservados: sufixo não duplicado, `Tab` incremental, `Esc`, rejeição de obsoleto (testes adaptados sem enfraquecer).
10. Experimento de gatilhos executado em uso real na máquina de referência (homologação manual), com taxas de aceite e ruído por gatilho; gatilhos padrão e parâmetros de debounce registrados em [decisions.md](../decisions.md).
11. PNGs dos 18 cenários inspecionados para ghost de uma e várias linhas nos dois temas.
12. `InlineCompletionTextBlock`, `CompletionPanel`, `GhostLayer` e o fluxo antigo removidos sem chamadores remanescentes.
13. Suíte regular e build das três variantes aprovados; documentação 17, 21, 22, 24 e matriz atualizadas; AC-12 e AC-13 promovidas ou revisadas.

## Riscos

| Risco | Mitigação |
| --- | --- |
| Sugestões percebidas como ruído | Gatilhos medidos; limiar de confiança; opção de desligar IA inline |
| Latência em CPU | Política de latência; camada 0 sempre disponível |
| Ghost multilinha no AvaloniaEdit (caret, hit-testing, virtualização) | Protótipo no 5.5; alternativa em camada de fundo |
| IME e composição | Gating por composição; homologação manual |
| Modelos de histórico expondo literais | Somente statements digitados pelo usuário na mesma conexão/banco, filtro de privacidade, respeito ao opt-out de histórico |
| Consumo de CPU/bateria em digitação longa | Debounce adaptativo; métricas de requisições por minuto; opção por conexão ou global |
| Regressão de comportamentos existentes | Testes atuais adaptados antes de remover o fluxo antigo |

## Fora do escopo

Ranker aprendido, persistência de uso, decodificação restrita, sugestões em campos fora do editor principal.
