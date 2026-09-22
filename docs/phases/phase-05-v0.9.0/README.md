# Fase 5 — v0.9.0: IA local e produtividade contextual

**Situação:** Experimental.

A camada de IA local já participa do catálogo de localização nos quatro idiomas (`pt-BR`, `en`, `es`, `zh-CN`), com fallback em inglês. Isso encerra a meta transversal de tradução, mas não altera o status experimental nem substitui a homologação de modelos e hardware reais.

## Objetivo

Assistência técnica local integrada à IDE, mantendo o autocomplete determinístico como base.

## Escopo incluído (IDs do catálogo)

- ADV-09 — modelos ONNX, ghost text aceito por Tab, propostas de chat revisáveis com diff, catálogo multimodelo e seleção de CPU/GPU/NPU.
- EDT-02 (extensão preemptiva) — sugestão inline sem solicitação explícita.

## Fora de escopo

Chatbot genérico, execução automática de sugestões, envio implícito a serviços externos, garantia de otimização sem explain/medição e obrigatoriedade de GPU.

## Antecipações técnicas presentes no código

Existem `LocalModelAiChatService`, `OnnxLocalModelRuntime`, ghost text e propostas com diff. **Inferência CPU tem registros; GPU não está homologada e CUDA compilado não significa geração validada.** A existência do painel não torna a assistência pronta.

## Critério de aceite

Casos nos quatro idiomas para cada ação sem alterações extras não solicitadas; Tab/Escape/undo e descarte de resposta obsoleta; funcionamento sem modelo instalado; opt-out impedindo contexto indevido; cancelamento isolado entre chat e autocomplete; latência e memória documentadas por hardware e modelo.

## Dependências

Fase 4 aceita, modelo compatível externo, contexto determinístico, política de dados e avaliações reproduzíveis.

## Documentos relacionados

- [ONNX/chat](../../23-onnx-slopcoder.md) · [IA local multimodelo](../../26-ia-local-multimodelo.md) · [Autocomplete preemptivo](../../auto-complite/preemptive-autocomplete.md) · sub-fase [5](../../auto-complite/phases/phase-5-preemptive.md)

## Pendências de homologação real

GPU/NPU, revisão linguística de domínio, fidelidade de todas as ações e medições de latência em hardware real continuam abertas.
