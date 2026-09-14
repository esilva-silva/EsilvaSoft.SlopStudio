# Consultas avançadas — incremento da v0.6.0

## Fluxo textual

O Console aceita `db.getCollection("orders").aggregate([...])`. O modo Agregação aceita o array de stages e usa a conexão, banco e coleção vinculados à aba. O Explorer continua navegando sem executar consultas. F5 executa o conteúdo completo; Ctrl+Enter mantém o contrato de seleção/statement do Console e seleção/conteúdo completo em Agregação.

O catálogo local cobre `$match`, `$group`, `$project`, `$lookup`, `$unwind`, `$facet`, `$sort`, `$limit`, `$skip`, `$set`, `$unset` e `$count`. Em Ctrl+Espaço, posições de stage, predicado, acumulador e referência `$campo` recebem sugestões distintas. O catálogo é uma ajuda lexical, não uma prova de compatibilidade com a versão do servidor ou de evolução do schema ao longo de todos os stages.

## Validação sem execução

**Opções → Validar sintaxe** funciona desconectado. A seleção, quando existe, é validada isoladamente; caso contrário, valida-se o documento inteiro. O diagnóstico informa linha/coluna relativas ao trecho validado e seleciona a posição no editor. Texto, contexto ou modo alterados durante a operação invalidam a resposta.

O parser roda em worker, aceita cancelamento e limita a entrada a 1 milhão de caracteres. No modo Agregação, verifica array de stages, um operador por documento e pipelines aninhados de `$facet`, `$lookup` e `$unionWith`. Construtores BSON não são executados. Uma mensagem de sintaxe válida não garante tipos BSON, operadores suportados, permissões nem execução bem-sucedida no servidor.

**Opções → Formatar JSON/query/script** conserva a operação já existente, incluindo seleção, cancelamento e Ctrl+Z. Syntax highlighting e pareamento usam o editor AvaloniaEdit existente.

## Proteção de escrita e análise

O executor direto de aggregation e o cursor de leitura do Console bloqueiam `$out`/`$merge` antes de enviar o pipeline. A verificação percorre posições reais de pipelines aninhados; objetos em `$literal` não são confundidos com stages. Operadores desconhecidos permanecem editáveis e sua compatibilidade é decidida pelo servidor.

No modo Agregação, **Opções → Analisar pipeline (explain)** solicita o plano do conteúdo completo com `queryPlanner`, limite de servidor de 30 segundos e o destino capturado antes do primeiro await. O plano BSON aparece em **Mensagens**, com conexão, banco e coleção. Resultados anteriores permanecem disponíveis. O cancelamento pertence à aba; uma análise não redireciona o retorno para outra aba.

Essa ação não apresenta tempo real de execução nem contagem real de documentos examinados. Não é um explain gráfico e não acrescenta `.explain()` à API JavaScript do Console. Referências oficiais: [aggregation pipelines](https://www.mongodb.com/docs/manual/core/aggregation-pipeline/), [explain](https://www.mongodb.com/docs/v8.0/reference/command/explain/) e [formatos de planos](https://www.mongodb.com/docs/manual/reference/explain-results/).

## Evidência e pendências

- `AdvancedAggregationTests`: bloqueio de escrita antes de conectar, estrutura, literais, 12 sugestões de stages, distinção contextual, construtores BSON, posição de erros, cancelamento e snapshot de explain entre abas.
- `ConsoleMongoIntegrationTests.PhaseTwoTwelveStagesJoinArraysAndFacetUseRealServer`: servidor MongoDB portátil, fixture independente de clientes/pedidos, pipeline de 12 tipos de stages; resultado esperado Bia/5 e contagem 2; plano real, origem preservada e ausência da coleção de saída bloqueada.
- `AdvancedAggregationUiTests`: controle real desconectado, seleção do operador inválido, ausência de chamadas MongoDB, recuperação após corrigir e 18 PNGs `aggregation-diagnostic-*` em claro/escuro, 960/1366/1920 e escalas 100/150/200%.

O incremento não encerra o aceite da Fase 2. Faltam consolidar autocomplete por schema/coleção efetiva em expressões complexas e em joins, histórico do modo Agregação, diagnósticos úteis de erro do servidor e jornadas de execução parcial com os pipelines reais. O catálogo funcional também mantém requisitos mais amplos (exportação C#/mongosh, apoio visual e explain gráfico) que não foram entregues aqui. Homologação nativa Windows/Linux e acessibilidade não são comprovadas por Headless.
