# Backlog — modo Aggregation na tela inicial

**Origem:** IDs AGG-01/02/03/04. **Situação:** modo desativado na interface; implementação preservada como **antecipação técnica**.

No roadmap oficial atual a v0.6.0 é [organização dos projetos e autocomplete básico](../phases/phase-02-v0.6.0/README.md). O trabalho de agregação, antes associado à v0.6.0, não pertence à fase ativa e não tem fase atribuída.

## Estado real da implementação — antecipação técnica

A implementação é **parcialmente funcional e não está concluída**. O que existe:

- `AggregationQuery` (Core) com validação de limite.
- `MongoQueryExecutor.AggregateAsync` e `ExplainAggregationAsync` (`queryPlanner`, BSON bruto).
- `AggregationPipelineValidator` — bloqueia `$out`/`$merge`, percorre `$facet`/`$lookup`/`$unionWith`, profundidade máxima 64.
- `AggregationFieldInference` (Application) — inferência local de campos derivados, sem executar construtores ou variáveis JavaScript.
- Catálogo de 12 stages, validação offline com localização do erro, histórico de agregação e diagnósticos seguros.

O que **não** está concluído: fixtures reais de todas as famílias de stages, explain gráfico/SBE, catálogo completo de operadores e apoio visual à construção do pipeline (AGG-02). A auditoria histórica está preservada em [27 — Consultas avançadas](27-consultas-avancadas.md); ela registra uma meta textual validada em 14/09/2026, **não** o encerramento do requisito.

## Decisão

O modo **Agregação** foi desativado no seletor de modos da tela inicial, junto com o modo Script. O `ComboBox` deixou de ser exibido e o único modo oferecido é **Console**.

Nada foi apagado: parser, validador, inferência, contratos, histórico e explain permanecem no código e continuam cobertos pela suíte de testes, que atribui `Mode = "Agregação"` diretamente e serve de evidência de que o caminho segue íntegro.

## Escopo futuro, quando houver fase

Reativar o modo no seletor, fechar fixtures reais por família de stage, decidir sobre explain gráfico e sobre AGG-02, e homologar em servidor real. Até lá, nenhum documento pode descrever a agregação como escopo concluído de uma fase.

## Documentos relacionados

[03 — Catálogo funcional](../03-catalogo-funcional.md) · [06 — Editor/BSON](../06-editor-bson-e-uuid.md) · [22 — Highlighting](../22-syntax-highlighting.md) · [10 — ADRs](../10-decisoes-arquiteturais.md)
