# Plano executável da v0.11.0

**Em desenvolvimento inicial.** O lote 0 tem spikes isolados; o lote 1 tem adapters de cofre compostos, ainda sem homologação nativa completa; e o lote 2 tem persistência de política, evaluator e registry interno parcial. Nenhum lote está concluído; MCP, runtime, chat, providers, protocolo externo, migração de credenciais, tools expostas e todos os ACs continuam pendentes. Prefixo de projetos omitido nas tabelas: `EsilvaSoft.SlopStudio`. Cada lote deve terminar com revisão de diff, documentação e evidência do seu gate antes de liberar o seguinte.

## Agentes incorporados ao plano

A execução de cada lote usa os responsáveis, colaboradores, revisores e evidências da [matriz 20 — Agentes e execução](20-agentes-e-execucao.md). A sessão principal segue [goal-orchestrator](../../../agents/goal-orchestrator.md) e o [protocolo comum](../../../agents/phase-7-protocol.md); QA e revisão são gates de todo lote. [Claude Code](../../../agents/claude-code.md) usa os mesmos contratos por adapters locais. Esta preparação documental não fecha lote nem AC.

Os responsáveis são: arquitetura (0), persistência/segurança (1/11), Tool Registry (2), MCP (3/4), runtime (5), UI/UX (6), providers (7/8), ONNX (9), domínio MongoDB (10) e QA (12). Cada despacho inclui lote, dependências, arquivos exclusivos, requisitos/ACs e cenários de falha; a matriz detalha as entregas e a revisão independente.

## Sequência e dependências

```text
0 Contratos/spikes -> 1 Cofre e identidade -> 2 Registry/políticas/auditoria
                                              -> 3 MCP read-only -> 4 Interoperabilidade
                                              -> 5 Runtime -> 6 Chat nativo
                                                           -> 7 Codex / 8 Claude / 9 Local
4 + 6 + 7 + 8 + 9 -> 10 Escritas protegidas -> 11 Hardening -> 12 Aceite integrado
```

Cofre, permissões e auditoria vêm antes de expor dados, não depois dos providers. O servidor read-only é o primeiro marco utilizável; não representa a conclusão da versão. A UI pode ser construída com provider de teste sem credenciais. Sem gate aprovado, a feature continua indisponível, preservando a IDE atual.

| Lote / objetivo | Dependências | Projetos, interfaces e classes principais | Testes e critério de conclusão |
| --- | --- | --- | --- |
| 0 — Fechar contratos e spikes de integração | Este plano | Core/Agents: DTOs; Application/Agents: portas; spike isolado de SDK/processo sem dados privados | Fixar binário Codex/schema, SDK Anthropic, pacote MCP e lock/transitivas; provar controle de ferramentas nativas, backend seguro e MCP dual-era. ADRs revisadas e licenças aprovadas; nenhuma dependência promovida por suposição |
| 1 — Credenciais e principal autenticado | 0 | Parcial: Application `ISecretStore` e `IAgentCredentialProvider`; Core `SecretReference`; adapters de cofre e provider de credenciais compostos no DI. Pendentes: identidade integrada, migração versionada e homologação nativa completa. | Evidências históricas e limites no relatório 17; não equivalem a gate aprovado. Ainda exigir disponibilidade/ausência/bloqueio reais, falhas da migração, reinício, exclusão e concorrência nos dois SOs; sem fallback plaintext e com perfis legados recuperáveis. |
| 2 — Registry, autorização e auditoria | 1 | Parcial: contratos, política persistida, evaluator, ledger versionado e facetas DI no owner único; registry interno com `list_connections` fora do DI. Pendentes: integrar auditoria ao pipeline, identidade, schemas das demais tools, controle de saída, executor BSON literal e demais handlers. | Evidências por incremento em 17/18/19; nova execução deve registrar seus próprios resultados. Ainda exigir entradas extras/ENV/JS negadas, namespace e read-only, negação de saída, auditoria indisponível, mesmo handler/política para todos os ingressos, schemas versionados e fixtures por tool. Nenhuma tool é exposta por MCP. |
| 3 — MCP somente leitura | 2 | Novo McpServer: `McpToolAdapter`, `AgentBrokerClient`; Infrastructure: `AgentBrokerHost`; primeiros handlers do catálogo | STDIO sem ruído, cliente sem grant, dois clientes, host fechado, versões IPC incompatíveis, BSON/limites/deadlines. Descobrir e executar conjunto inicial sem segredos nem escrita |
| 4 — Compatibilidade externa e recovery | 3 | McpServer e broker; fixtures separadas MCP 2026-07-28/2025-11-25 | Codex e Claude Code/Desktop por configuração explícita nas versões homologadas; cliente de referência dual-era. Crash/EOF/revogação/cancelamento e ausência de replay. Publicar matriz exata; Copilot só anunciado se efetivamente testado |
| 5 — Runtime normalizado | 2 | Application: `IAgentRuntime`, `IAgentProvider`, `IAgentSession`, `IAgentContextProvider`; `AgentRuntime`, `AgentContextProvider`, filas limitadas | Provider falso determinístico, deltas/duplicação/ordem, deadlock tool-result, isolamento e starvation. Eventos e estados terminais conforme contrato; nenhum DTO externo fora do adapter |
| 6 — Chat/configuração/aprovação nativos | 5 | Desktop: `AgentChatViewModel`, `AgentSettingsViewModel`, `AgentApprovalViewModel` e Views; recursos/localização | Fluxos teclado/foco, troca de provider sem transferência de dados, aprovação expirada, estados vazios/erro, PNGs nos dois temas. UI usa apenas runtime/capabilities; controles não prometem auth inexistente |
| 7 — Adapter OpenAI/Codex | 0,1,5,6; 3 quando usa ponte MCP | Baseline: `OpenAiAgentProvider` e sessão API direta em Infrastructure.Agents/OpenAI; composition root. `CodexAgentSession`/`CodexAppServerClient` somente após gates específicos do App Server | API Key no cofre, credencial inválida/expirada, streaming, tools, orçamento e interrupção. App Server/login de assinatura condicionados a suporte oficial de produção, confinamento e armazenamento comprovados no lote 0; validar login/cancelamento/falha de processo somente nessa variante. Registrar capabilities e aceite efetivos; API Key não comprova login ChatGPT |
| 8 — Adapter Claude | 0,1,5,6 | Infrastructure.Agents/Anthropic: `ClaudeAgentProvider`, `ClaudeAgentSession`; API C# e loop no runtime | Chave inválida, stream fragmentado, argumentos completos, tool-result, timeout e orçamento. Sessão lógica e seleção de modelo; sem login de assinatura não autorizado. Agent SDK sidecar permanece alternativa de spike |
| 9 — Fachada local | 5,6 | Application: `LocalAgentProvider` sobre LocalAi.Core e serviços existentes; Infrastructure.LocalAi preservada | Offline, modelo ausente, FIM sem Chat/ToolCalling, preempção e autocomplete simultâneo. Nenhuma rede/fallback externo nem CTS global; assistente atual preservado |
| 10 — Escritas e índices controlados | 4,6,7,8,9; gates de 2 | Application handlers; Infrastructure: mutações unitárias/precondições e auditoria; Desktop aprovação | insert/update/delete/create/drop index contra MongoDB descartável real; read-only, RBAC, `_id_`, concorrência, replay e crash pós-envio. Ticket único e pré-condição atômica; resultado incerto explícito |
| 11 — Hardening e distribuição | 10 | Infrastructure, Infrastructure.Agents, McpServer, Desktop, eng/CI | Threat model, payload/stream hostil, secret scans, ambientes mínimos de subprocessos, atualização proxy/IDE, rollback de pacote sem corromper sessão. SBOM/avisos/tag/hash revisados; IDE funciona sem agentes |
| 12 — Aceite completo | 11 | UnitTests/Benchmarks e harness de integração a decidir conforme volume; docs | Evidências AC-01..20, plataformas nativas, versão/conta/modelo registrados com dados sintéticos. Nenhuma pendência de segurança tratada como homologada por mocks |

## Divisão em mudanças revisáveis

Cada lote pode gerar PRs menores por contrato, handler ou superfície. Primeiro adicionar DTO/interface e teste comportamental; depois adapter/caso de uso; por último entrada de UI ou registro da tool. Não publicar uma tool antes de existir autorização, saída limitada e teste de falha. Evitar introduzir projetos extras só para acomodar uma classe.

No lote 2, implementar primeiro `list_connections`/metadados, depois codec literal e find/count, depois schema/sample/distinct/explain/índices. Schema por amostragem exige consentimento apropriado. Aggregate e estatísticas adicionais entram somente após seus gaps; operações administrativas e script livre ficam fora da baseline. No lote 10, cada operação é liberada separadamente após testes de intenção/aprovação/desfecho.

## Decisões que exigem evidência futura

| Questão | Responsável e momento | Saída obrigatória / fallback seguro |
| --- | --- | --- |
| App Server tem suporte produtivo e permite confinamento sem tools alternativas? | Arquitetura/segurança, lote 0 | Relatório reproduzível antes de habilitar a variante; manter API direta como baseline, com auth/capabilities e aceite explícitos, sem prometer equivalência com login ChatGPT |
| SDK MCP suporta as duas eras e clientes escolhidos? | Infra/QA, lotes 0/4 | Versões fixadas e fixtures; incompatibilidade impede anunciar cliente, não autoriza misturar protocolos |
| Secret Service indisponível no Linux alvo? | Segurança, lote 1 | Modo memória explícito ou provider indisponível; IDE operacional |
| Pré-condição update/delete é atômica nos documentos escolhidos? | Domínio MongoDB, lote 10 | Fixture concorrente real; negar quando não há estratégia segura |
| Licença de binário/SDK/transitivas permite distribuição? | Revisão de dependências, 0/11 | NOTICE e licença da versão; não incorporar componente sem verificação |

Streamable HTTP é incremento opcional posterior ao lote 4, dependente do mesmo broker e de revisão específica de autenticação/Origin/Host/TLS. Não condicionar o primeiro MCP à hospedagem remota. Persistência de transcript, shell, file editing e subagents requerem novos requisitos e não são ativados por um provider reportá-los.

## Validação por mudança

Executar restore locked, build e testes conforme AGENTS.md. Atualizar catálogo, matriz, acompanhamento e ADR quando comportamento mudar. UI exige PNG real inspecionado, Windows/Linux e testes de foco; testes headless não substituem homologação nativa. Providers reais são testados somente com credenciais autorizadas, fora da CI comum e sem gravá-las em artefatos. [Matriz de testes](11-plano-de-testes.md).
