# Fase 5 — Autocomplete preemptivo

**Planejada, revisada em 15/09/2026.** Três entregas obrigatórias e independência entre geradores. [Arquitetura detalhada](../preemptive-autocomplete.md).

## Justificativa da divisão

Conservar numeração permite comparar com plano anterior; 5.1 pode ocorrer após a Fase 2, sem esperar IA. Presenter compartilhado nasce no 2, também usado pelo 4. Coordinator pertence a Traditional Preemptive; AI Preemptive integra por contrato, sem segundo renderer/scheduler.

## 5.1 Traditional Preemptive Completion

Dependências: dados consolidados e Fase 2 (contexto, ranking, presenter, flags). Agente: Traditional Preemptive; revisão Architecture, testes/performance desde o começo.

- P51: coordinator por editor, gating e uma pendência substituível; coalescer eventos da mesma edição.
- P52: TraditionalPreemptiveCompletionProvider reutiliza CompletionService/ranker com Peek, prefixo estrito e confiança; sem rede/modelo/histórico de valores.
- P53: conectar presenter; typeahead de candidato concluído; Tab/Esc/undo, recuperação por nova edição e supressão de âncora rejeitada.

Aceite: funcional com IA/modelo ausente; ambiguidade e catálogo truncado não geram falso candidato único; zero chamadas remotas/IA em sequência de digitação; sugestão antiga nunca aplicada; computação p95 ≤ 5 ms e edição → ghost ≤ 20 ms como metas a medir. PNGs reais nos dois temas e 18 combinações inspecionados. Correções antes do cursor só pela lista até existir prévia de substituição.

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
