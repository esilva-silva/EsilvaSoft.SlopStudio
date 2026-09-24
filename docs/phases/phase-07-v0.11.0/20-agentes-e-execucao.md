# Agentes e execução da Fase 7

Preparação operacional revisada em **24/09/2026**. Complementa o [plano executável](10-plano-de-implementacao.md), sem concluir lotes nem AC-01..20. Os perfis abaixo implementam o repositório; não são a capability `SubAgents` do produto.

## Entrada e responsabilidades

A sessão principal segue [goal-orchestrator](../../../agents/goal-orchestrator.md) e o [protocolo comum](../../../agents/phase-7-protocol.md). O catálogo canônico está em [agents/README](../../../agents/README.md); a preparação para Claude Code está no [guia de execução](../../../agents/claude-code.md). O orquestrador atribui um proprietário por arquivo, aceita entregas, serializa integração e atualiza esta fase e a [memória](../../memory/phase-7.md).

Cada lote abaixo mantém as dependências, projetos, testes e critérios da tabela do plano 10. QA e code-review são obrigatórios em todo gate; o revisor não é o autor da mudança. Se só houver uma sessão, registrar revisão sequencial sem alegar independência. `documentation-agent` integra evidências, índice e status no fim de cada lote. Referências a ACs indicam cobertura, não aprovação automática.

| Lote | Responsável principal | Colaboração delimitada | Revisão do gate | Evidência obrigatória e ACs relacionados |
| --- | --- | --- | --- | --- |
| 0 | [architecture-agent](../../../agents/architecture-agent.md) | mcp-integration-agent: SDK/protocolo; agent-provider-agent: APIs/processos; persistence-security-agent: confinamento/cofre | code-review-agent + QA de contrato | Spikes reproduzíveis, versões/schema/lock, licenças e ADRs; AC-02/05/06/07/08/20 |
| 1 | [persistence-security-agent](../../../agents/persistence-security-agent.md) | architecture-agent: identidade/portas; QA: falha e migração | code-review-agent + QA nativo | Cofres por SO, migração recuperável, scan de segredos, restart; AC-03/07/08/17 |
| 2 | [tool-registry-agent](../../../agents/tool-registry-agent.md) | persistence-security-agent: grants/ledger; mongodb-domain-agent: codec/handlers; architecture-agent: DI | code-review-agent + QA de segurança | Default deny, saída autorizada, auditoria integrada, schemas fechados, equivalência dos ingressos; AC-03/12/13/14 |
| 3 | [mcp-integration-agent](../../../agents/mcp-integration-agent.md) | tool-registry-agent: ingressos; persistence-security-agent: IPC/identidade; MongoDB: leitura | code-review-agent + QA de transporte | STDIO read-only real, IPC autenticado, host indisponível e limites; AC-01/02/03/13/14/17 |
| 4 | mcp-integration-agent | QA: clientes/versões; persistence-security-agent: revogação | code-review-agent + QA de interoperabilidade | Cliente independente e clientes reais escolhidos, EOF/crash, sem replay; AC-02/11/17 |
| 5 | [agent-runtime-agent](../../../agents/agent-runtime-agent.md) | architecture-agent: portas; tool-registry-agent: tools; performance-agent: filas | code-review-agent + QA de concorrência | Provider falso, terminais/ordem/deduplicação, tool-result sem deadlock, cancelamento isolado; AC-04/09/11/14/15 |
| 6 | [ui-ux-agent](../../../agents/ui-ux-agent.md) | agent-runtime-agent: eventos; persistence-security-agent: consentimento/aprovação | code-review-agent + QA visual | PNGs inspecionados claro/escuro, foco/teclado, capabilities, aprovação expirada; AC-04/07/09/10/11/12 |
| 7 | [agent-provider-agent](../../../agents/agent-provider-agent.md) | agent-runtime-agent: stream; persistence-security-agent: API Key/confinamento | code-review-agent + QA de provider | OpenAI real autorizado, limites e falhas; App Server condicional, sem alegar login entregue por API Key; AC-05/07/08/09/11/15/20 |
| 8 | agent-provider-agent | agent-runtime-agent: tool loop; persistence-security-agent: chave | code-review-agent + QA de provider | Claude API real autorizado, fragments/tool-result, orçamento, auth inválida; AC-06/07/08/09/11/15/20 |
| 9 | [onnx-ai-agent](../../../agents/onnx-ai-agent.md) | agent-runtime-agent: fachada; autocomplete-agent: regressão; performance-agent: preempção | code-review-agent + QA offline | Sem rede, modelo ausente, FIM sem capabilities inventadas, autocomplete simultâneo; AC-09/15/16 |
| 10 | [mongodb-domain-agent](../../../agents/mongodb-domain-agent.md) | tool-registry-agent: approval; persistence-security-agent: ledger; ui-ux-agent: confirmação | code-review-agent + QA Mongo real | Pré-condição atômica, RBAC/read-only, índice `_id_`, replay/crash e resultado incerto; AC-12/19 |
| 11 | persistence-security-agent | mcp-integration-agent: distribuição; agent-provider-agent: dependências; performance-agent: limites; documentation-agent: NOTICE | code-review-agent + QA de resiliência | Threat model, payload hostil, canários, SBOM/licenças, IDE degradada e plataformas; AC-03/08/15/17/20 |
| 12 | [qa-testing-agent](../../../agents/qa-testing-agent.md) | documentation-agent: rastreabilidade; responsáveis anteriores: sanar lacunas | code-review-agent + aceite do orquestrador | Matriz integral AC-01..20, incluindo AC-18 (roadmap/links); nenhum mock substitui evidência nativa |

`code-organizer` atua apenas em refatoração mecânica delimitada após estabilizar contratos. `scripting-console-agent` revisa fronteiras com Jint/mongosh quando houver impacto, sem expor script livre nas tools. Nenhum deles amplia o escopo da fase. O guia de [capacidade](../../../agents/capabilities.md) orienta risco e escalonamento, sem impor modelos que o ambiente não oferece.

## Unidade de trabalho e integração

```text
Task: P7-L02-01 — Integrar auditoria ao registry interno
Owner: tool-registry-agent
Dependencies: portas de ledger/política revisadas; conferir estado atual no checkout
Scope: pipeline do registry e fixtures; persistência/DI pertencem aos responsáveis designados
Requirements / ACs: contrato 08/09, AC-03/12/14 (evidência parcial)
Acceptance: negar antes do despacho se intenção falhar; preservar resultado incerto após envio;
            saída saneada, correlação de intenção/desfecho, nenhum replay
Gate: nenhuma tool exposta enquanto identidade, política e saída não forem validadas
Evidence: comandos, exit codes, cenários, artefatos sanitizados, limitações
Handoff: QA valida; code-review revisa; orquestrador integra e atualiza memória
```

O exemplo é um despacho, não uma declaração de implementação. Revalidar primeiro os recortes existentes dos lotes 0–2, evitando duplicar cofre, grants e ledger. Há persistência e facetas compostas; ainda é necessário integrar o pipeline e demonstrar seus gates. O detalhamento de evidências continua em [17](17-validacao-da-meta.md), [18](18-persistencia-de-autorizacao.md) e [19](19-persistencia-auditoria.md).

Estados por tarefa: pendente → em execução → em revisão → concluída, ou bloqueada com motivo e próximo passo. Uma tarefa concluída pode produzir apenas evidência parcial do lote. Registrar data, revisão do checkout, proprietário, AC, comando/cenário, resultado, artefato e limitação. O orquestrador só aprova o gate após revisão; dependências incompletas permitem preparação isolada, nunca exposição de ferramenta ou anúncio de suporte.

## Preparação para Claude

Abrir Claude Code na raiz e usar o prompt de retomada do [guia](../../../agents/claude-code.md). `CLAUDE.md` importa governança e orienta a sessão principal; `.claude/agents` contém adapters dos especialistas. Não copiar credenciais nem configurar MCP do produto para conseguir desenvolver.

Preparação dos arquivos e verificação estática não são homologação do Claude Code instalado. A execução do provider Claude no lote 8 continua exigindo implementação e chamada API real autorizada. Este trabalho não concede permissões MongoDB nem fecha qualquer aceite funcional.
