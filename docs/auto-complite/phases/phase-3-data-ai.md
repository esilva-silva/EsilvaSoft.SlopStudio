# Fase 3 — Disponibilização de dados para IA

Roadmap: v0.9.0 · Depende de: Fases 1 e 2 · Habilita: Fase 4

## Objetivo

Criar a camada que transforma `CompletionContext` + catálogo em um prompt pequeno, relevante e determinístico, com contratos versionados por modelo, orçamento de tokens e avaliação empírica de formatos. Sem mudança de UI; o fluxo de IA atual continua usando o contrato v1.

## Situação atual

- `AutocompleteContextBuilder` gera cabeçalho de texto plano com listas (nomes conhecidos, campos de resultados, comandos, Input, histórico), limitado por caracteres.
- Esse cabeçalho é o contrato de treino dos pacotes SlopCoder.
- Tokenização do prefixo/sufixo completos antes do corte; nenhuma seleção por relevância; nenhum harness de avaliação no repositório.

## Incrementos

| # | Entrega | Resultado verificável |
| --- | --- | --- |
| 3.1 | `IAiContextContract` e congelamento de `editor-context-v1` | Teste-ouro byte a byte contra a saída atual |
| 3.2 | `RelevantContextSelector`: fatos, relevância, janela sintática, statements semelhantes | Testes de seleção |
| 3.3 | `AiBudget`, estimativa de tokens por modelo, cache de blocos tokenizados | Propriedade: orçamento nunca excedido |
| 3.4 | Contratos B–E ([ai-context.md](../ai-context.md#formatos-avaliados)) | Prompts-ouro |
| 3.5 | Metadata `contextContract`/`supportsRepositoryContext`; validação no adapter | Pacotes antigos continuam válidos |
| 3.6 | Dataset sintético, harness de avaliação, relatório e decisão de formato | Relatório registrado |

## Alterações

| Projeto | Arquivo | Alteração |
| --- | --- | --- |
| Core | `LocalAi.cs` | Campos opcionais no `LocalModelMetadata` |
| Application | `AutocompleteContextBuilder.cs` | Encapsulado como contrato v1, sem mudança de saída |
| Application | `QwenFimPromptBuilder.cs`, `DeepSeekFimPromptBuilder.cs` | Aceitam blocos pré-tokenizados; marcadores em cache |
| Application | `BasicAutocompleteProvider.cs` (`CompletionPrivacy`) | Aplicação por fato |
| Infrastructure | `LocalModelCatalog.cs` | Leitura dos novos campos |
| Infrastructure | `ModelAdapters.cs` | Contratos suportados; verificação de tokens de repositório no Qwen |
| Tests | `Benchmarks/Ai/*` | Harness e dataset |

## Novos componentes

`AiFact`, `AiFactSet`, `AiFactKind`, `IRelevantContextSelector`/`RelevantContextSelector`, `EditorWindowBuilder`, `SimilarStatementFinder`, `AiBudget`, `ITokenCounter`, `TokenRatioEstimator`, `TokenizedBlockCache`, `IAiContextContract`, `EditorContextV1Contract`, `CompactFactsContract`, `TypeDeclarationsContract`, `RepositoryFilesContract`, `JsonSchemaContract`, `AiPrompt`, `AiPromptReport`, `AiContextEvaluationHarness`.

## Fluxo

[ai-context.md — Pipeline](../ai-context.md#pipeline).

## Dependências

- Fase 1: catálogo com schema, índices e validator.
- Fase 2: `CompletionContext`, árvore sintática e resolvedor de alvo.
- Modelos externos para a avaliação (`SLOP_QWEN_MODEL`, pacotes SlopCoder e Qwen base).

## Performance

- Seleção sobre fatos já em memória; nenhum I/O.
- Corte por estimativa antes de tokenizar; contagem exata depois.
- Blocos estáveis tokenizados uma vez por modelo/contrato.
- Medir: seleção + builder, tokenização por tamanho, erro da estimativa, tokens por contrato.

## Testes

- Prompt-ouro por contrato e fixture; v1 idêntico ao atual para as fixtures de `PredictiveAutocompleteTests` e `LocalAutocompleteTests`.
- Seleção: 40 coleções no banco e somente a alvo incluída; filhos do caminho parcial priorizados; statement semelhante inteiro ou ausente.
- Propriedade de orçamento com tamanhos aleatórios de fatos e janelas.
- Privacidade por fato e no prompt final; marcadores reservados.
- Metadata: pacotes sem campos novos continuam `Valid`; contrato desconhecido → `Invalid` com mensagem.
- Harness `Explicit` com relatório JSON/Markdown.

## Critérios de aceite

1. `editor-context-v1` produz saída byte a byte idêntica à implementação atual em todas as fixtures existentes.
2. Nenhum prompt excede `ContextTokens` em 10 000 casos gerados com sementes registradas.
3. Mesma entrada produz o mesmo prompt em execuções repetidas.
4. Fatos de coleções que não são o alvo nunca aparecem (exceto `from` de `$lookup`/`$unionWith`/`$graphLookup`).
5. Valores de resultados e amostras nunca aparecem no prompt (verificado contra as fixtures).
6. Erro da estimativa de tokens medido por modelo e dentro da margem configurada, ou margem ajustada com dados.
7. Harness executa v1 e ao menos um formato alternativo no modelo base disponível; ampliar a A–E só se houver hipótese/ganho. Registrar modelos indisponíveis; não bloquear v1 por exportação experimental ausente.
8. Decisão de contrato padrão para modelos base registrada em [decisions.md](../decisions.md); pacotes SlopCoder mantêm v1.
9. Seleção + builder dentro do orçamento revisado na máquina de referência.
10. Suíte regular e build das três variantes aprovados; [21](../../21-autocomplete-local.md) e [26](../../26-ia-local-multimodelo.md) atualizados.

## Riscos

| Risco | Mitigação |
| --- | --- |
| Dataset sintético não representativo | Categorias balanceadas; revisão com cenários reais anonimizados pelo desenvolvedor, sem dados de clientes |
| Modelos ajustados dependentes de v1 | Contrato por modelo; novo formato só com avaliação/treino |
| Variação da razão caracteres/token | Estimador por modelo com média móvel e margem |
| Tokens de repositório ausentes em algumas exportações | Verificação no adapter; contrato D indisponível nesses pacotes |
| Tempo de avaliação alto | Subconjuntos por categoria; execução manual documentada |
| Histórico com dados sensíveis | Filtro de privacidade; limite de dois statements da mesma conexão/banco; opt-out de histórico respeitado |

## Fora do escopo

Provider `Ctrl+;`, streaming, prefix cache, mudanças de UI.


## Revisão de tarefas

[A31–A34](../execution-plan.md) são os lotes obrigatórios. B–E acima são candidatos de pesquisa, não cinco implementações obrigatórias. Cache BPE não concatena blocos sem fronteiras provadas; serviço ONNX controla tokenizer/lifetime. Fonte learned participa como metadado probabilístico, sem valores; R41 entrega contrato de orçamento/tokenização antes de A33. O formato serializado v1 é preservado para a mesma entrada, distinguindo seleção de fatos.
