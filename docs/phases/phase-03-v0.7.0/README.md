# Fase 3 — v0.7.0: autocomplete com IA

**Situação:** Planejada.

## Objetivo

Acrescentar sugestões assistidas por modelo local ao autocomplete determinístico, **sempre revisáveis e sem aplicação automática**.

## Escopo incluído (IDs do catálogo)

- EDT-02 (extensão) — sugestão explícita assistida por modelo sobre o mesmo pipeline determinístico da Fase 2.
- ADV-09 (recorte de autocomplete) — uso do runtime local já existente para completar, sem chat e sem ghost text preemptivo.

## Fora de escopo

Chat, ghost text preemptivo, catálogo multimodelo e seleção de hardware — pertencem à [Fase 5](../phase-05-v0.9.0/README.md). Execução automática de qualquer sugestão é proibida em todas as fases.

## Antecipações técnicas presentes no código

Runtime ONNX (`Infrastructure.LocalAi`) e serviço de IA em `Application` existem e são usados por recursos experimentais. Sua existência **não** antecipa o aceite desta fase.

## Critério de aceite

Sugestão de IA sempre exibida como proposta revisável; funcionamento preservado sem modelo instalado; cancelamento isolado por aba; nenhuma alteração de texto sem confirmação explícita do usuário; latência documentada por hardware e modelo.

## Dependências

Fase 2 concluída: núcleos separados e autocomplete determinístico estável.

## Documentos relacionados

- [ONNX: SlopCoder, CPU/GPU e chat do editor](../../23-onnx-slopcoder.md)
- [Autocomplete por IA](../../auto-complite/ai-autocomplete.md) e [contexto para IA](../../auto-complite/ai-context.md)
- Sub-fases do subsistema: [3](../../auto-complite/phases/phase-3-data-ai.md) e [4](../../auto-complite/phases/phase-4-ai-autocomplete.md)

## Validação manual transferida

A avaliação de qualidade de sugestão por corpus reproduzível e revisão humana é critério da [Fase 8 / v0.12.0](../phase-08-v0.12.0/README.md).
