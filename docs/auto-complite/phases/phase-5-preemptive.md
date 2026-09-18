# Fase 5 — Autocomplete preemptivo

**Planejada, revisada em 15/09/2026.** Três entregas obrigatórias e independência entre geradores. [Arquitetura detalhada](../preemptive-autocomplete.md).

## Justificativa da divisão

Conservar numeração permite comparar com plano anterior; 5.1 pode ocorrer após a Fase 2, sem esperar IA. Presenter compartilhado nasce no 2, também usado pelo 4. Coordinator pertence a Traditional Preemptive; AI Preemptive integra por contrato, sem segundo renderer/scheduler.

## 5.1 Traditional Preemptive Completion — CONCLUÍDO em 18/09/2026, com pendências abertas

Dependências: dados consolidados e Fase 2 (contexto, ranking, presenter, flags). Agente: Traditional Preemptive; revisão Architecture, testes/performance desde o começo.

- P51: **entregue.** `InlineCompletionCoordinator` por editor, debounce por `TimeProvider`, uma pendência substituível por editor; coalescer eventos da mesma edição.
- P52: **entregue.** `TraditionalPreemptiveCompletionProvider` reutiliza `CompletionService`/ranker com `MetadataAccess.Peek`, determinístico, sem rede/ONNX/I-O; `InlineCompletionConfidence` decide abstenção por prefixo vazio, catálogo truncado, palavra completa, snippet, mais de uma continuação estrita, continuação fora do top-1 ou margem top-1/top-2 abaixo de 0,20.
- P53: **entregue** via `InlineCompletionPolicy`/`InlineCompletionEditorState`; typeahead, Tab/Esc/undo e recuperação por nova edição cobertos por teste.

**Mudança de comportamento não prevista no plano original:** a ordem de fonte padrão do ghost passou a ser determinístico → dicionário lexical local → IA. A IA automática só é alcançada com `InlineUseAi = true` **e** modelo em estado `Ready` (política LoadedOnly); nada nesse caminho carrega, troca ou inicializa modelo por digitação. `AiAutocompleteProvider` foi preservado, apenas deixou de ser a fonte padrão.

Aceite verificado: funcional com IA/modelo ausente; ambiguidade e catálogo truncado não geram falso candidato único; zero chamadas remotas/IA em sequência de digitação; sugestão antiga nunca aplicada (47 testes novos, incluindo relógio falso — 20 teclas abaixo do debounce não geram computação e a pausa gera exatamente uma —, os sete gatilhos de cancelamento, isolamento entre abas, funcionamento sem modelo/MongoDB, supressão pelo automático quando a lista explícita está aberta, combinações das três flags, LoadedOnly em três fases, migração, abstenção por ambiguidade/truncamento e aceite como operação única de undo sem executar consulta).

**Aceite NÃO verificado — não declarar cumprido:**

- **Computação p95 ≤ 5 ms: atendida** (catálogo de linguagem: p50 0,002 ms/p95 0,009 ms/máx 0,013 ms; com 200 campos de schema: p50 0,044 ms/p95 0,068 ms/máx 4,24 ms). Ver [performance](../performance.md).
- **Edição → ghost p95 ≤ 20 ms: NÃO atendida.** Medido em Headless com debounce de 50 ms: excedente de 17,6–27,1 ms além do debounce (primeira sugestão do processo 92/112/108 ms; aquecida 77/68/77 ms). O trabalho de geração em si é ~0,07 ms; o excedente é dominado pela resolução do temporizador do Windows (~15,6 ms) mais dois saltos de despachante. A meta não foi ajustada; o número medido fica registrado como não atendido.
- **PNGs reais nos dois temas e 18 combinações**: não produzidos nesta entrega.
- **IME**: `InlineCompletionEditorState.Composing` existe e é respeitado pelo coordinator, mas o editor **não publica** o estado de composição — `ImeComposing` é sempre falso em produção. A regra "não sugerir durante composição de IME" está testada e **não vale na prática**; é pendência de implementação.
- **UI de configuração**: `InlineEnabled`, `InlineUseTraditional` e `InlineUseAi` não têm controle em `AutocompleteSettingsWindow`; só editáveis via JSON persistido. Ver [configuration](../configuration.md).
- Correções antes do cursor continuam só pela lista; prévia de substituição não foi implementada nesta entrega.

## 5.2 AI Preemptive Completion

Dependências: A41–A44/R41 da Fase 4, P51/P53. Agente: AI Preemptive; runtime exclusivamente ONNX Runtime.

- P54: provider compartilhando seleção/builder/output da IA explícita, política LoadedOnly/Background.
- P55: debounce, deadline total, limite de tokens, perfil de latência com histerese; evitar sondagens/carga/troca automática.

Aceite: IA automática funciona com tradicional automático desligado; 20 teclas abaixo do debounce não geram inferência e pausa gera no máximo uma; modelo errado/descarregado não carrega; troca sob fila não cria sessão; prioridade interativa preempta; timeout/erro não abre popup; provider que ignora cancelamento não ressuscita ghost. Medir latência completa, não só TTFT.

## 5.3 Hybrid Preemptive Strategy

Dependências: P52–P55. Agente: Traditional Preemptive como dono do coordinator; AI Preemptive fornece política; Architecture decide integração.

- P56: padrão sequencial, tradicional forte encerra pedido; só ausência autoriza IA; um ghost por pedido.
- P57: matriz das flags, arbitragem com lista/snippet/IA explícita, instrumentação por origem e plano de migração.
- P58: homologação e relatório comparando padrão com extensão experimental desligada; limpeza do legado só após paridade.

Aceite: quatro combinações de flags automáticas; zero inferência após tradicional forte; nunca trocar tradicional visível por resposta IA tardia no padrão; Ctrl+. e Ctrl+; mantêm precedência; medir aceite/reversão/CPU/trocas visuais. Experimento concorrente não é requisito de implementação e não bloqueia entrega do padrão; sua promoção exige evidência real.

## Migração e arquivos

Core/Autocomplete.cs e WorkspaceSession.cs: flags aditivas conforme [configuration](../configuration.md). Application/Language/Inline proposto: coordinator/providers/cache de sugestão. Desktop/WorkspaceTabView.Autocomplete.cs: integra presenter comum; InlineCompletionTextBlock e GhostLayer só removidos no fim, sem chamadores. IncrementalCompletion e invariantes de CompletionSession são preservados.

Especificação completa por tarefa (arquivos, entrada, resultado, testes, aceite): [execution-plan.md](../execution-plan.md). Testes de falha, concorrência, recuperação, UI Headless e homologação nativa são separados; [testing](../testing.md). Nenhum número novo é medição nesta revisão.

## Riscos e mitigação

Ambiguidade → abstenção; schema parcial → sem certeza negativa; CPU/energia → gating e prazos; stale → geração; IME/teclado → homologação nativa; ghost multilinha → protótipo e retenção temporária do overlay; conflito de agentes → dono único por componente/lote. Prefix cache, ranker aprendido, inferência de valores históricos e restauração de Backspace ficam fora do aceite.
