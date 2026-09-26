# Fase 7 — v0.11.0: MCP e integração com agentes externos

**Situação: em desenvolvimento inicial.** O lote 0 tem evidência parcial de spikes; o lote 1 tem adapters isolados de cofre Windows/Linux; e o lote 2 tem persistência de política, auditoria, evaluator e registry interno de `list_connections`. As facetas de política, auditoria e evaluator já estão compostas no DI, usando o proprietário LiteDB único; a suíte focada no Windows passou com 77 aprovados em 23/09/2026. O ledger de auditoria ainda não está integrado ao registry, que não está registrado nem exposto. Não há integração MCP, runtime, chat, provider, migração de credenciais ou ferramenta MongoDB exposta nesta fase, e **AC-01 a AC-20 continuam pendentes**. Plano revisado em 23/09/2026 para o EsilvaSoft.SlopStudio (.NET 10/Avalonia, Windows/Linux, MIT). Spikes e limites: [`Codex App Server`](../../../eng/spikes/phase-07/codex-app-server/README.md), [`MCP C#`](../../../eng/spikes/phase-07/mcp-sdk/README.md), [`OpenAI API`](../../../eng/spikes/phase-07/openai-api/README.md), [`Anthropic SDK`](../../../eng/spikes/phase-07/anthropic-sdk/README.md), [`Credential Manager Windows`](../../../eng/spikes/phase-07/windows-credential-manager/README.md) e [`Secret Service Linux`](../../../eng/spikes/phase-07/linux-secret-service/README.md).

O lote 1 contém `ISecretStore` e `IAgentCredentialProvider` em Application, `SecretReference` opaca/versionada em Core e adapters isolados `WindowsCredentialSecretStore` e `LinuxSecretServiceSecretStore` em Infrastructure. O Windows passou **30 testes determinísticos** e tem **1 round-trip nativo ignorado** por `1312 / NoLogonSession`; o Linux passou **84 testes focados anteriormente**, sem D-Bus/Secret Service nativo exercitado. O lote 2 contém contratos de política, persistência versionada, evaluator e registry interno de `list_connections`, sem protocolo externo. `IAgentAuthorizationPolicyProvider`, `IAgentAuthorizationPolicyRepository` e `IAgentPermissionEvaluator` estão compostos no DI; a composição, evaluator, registry e persistência passaram **71 testes focados, 0 ignorados** no Windows. Os casos do registry cobrem alias de nome externo e revalidação do snapshot. O Tool Registry não é registrado nem exposto. Migração de credenciais, provider e UI continuam ausentes; isso não encerra lote nem AC.

Em execução local no Windows em 22/09–23/09/2026, `codex-cli 0.155.0-alpha.16` gerou 310 schemas JSON com hashes conferidos; probe separado recebeu resposta `initialize` válida, enviou `initialized` e observou saída normal após EOF. O spike MCP C# 2.2.0 passou restore locked/build e **10 testes STDIO** nas revisões 2025 e 2026: quatro SDK↔SDK e seis com cliente BCL independente, sem compartilhar dependência de protocolo no cliente, sempre contra servidor sintético com lista vazia. OpenAI SDK 2.14.0 passou build e cinco testes offline de streaming/tool-call contratual; não houve chamada real nem auditoria de vulnerabilidade. Anthropic 12.50.0 passou restore locked, build (0 avisos/erros), verificação de assinatura e inventário de dependências/licenças no alvo net10.0. `Tmds.DBus.Protocol` 0.94.1 foi identificado como candidato Linux MIT, sem execução D-Bus disponível. Essas provas permitem avançar dentro do lote 0, sem aprovar critérios de aceite. A documentação oficial ainda classifica o comando Codex App Server como experimental e sem suporte para produção; a baseline prevista é API direta com chave própria, sujeita a capabilities revisadas. `CODEX_HOME` isolado não comprova keyring nem sandbox/confinamento; a revisão de paths também não elimina TOCTOU. Nenhum código funcional de produto foi homologado e AC-01..AC-20 continuam pendentes. Consulte a [validação detalhada](17-validacao-da-meta.md) e a [matriz de validação](../../15-matriz-de-validacao.md#lote-0-da-v0110-spike-codex-confirmado-parcialmente--22092026).

## Prioridade vigente — Integração Claude (25/09/2026)

Decisão posterior do usuário: a prioridade da fase é **finalizar a integração com Claude**, priorizando a assinatura Claude Pro pelo binário oficial do Claude Code executado como subprocesso, com o modo Anthropic API separado e sem fallback silencioso para API Key. **Revisão de escopo no mesmo dia (25/09/2026):** o spike P7-CL0-01 foi executado no Windows ([relatório](../../../eng/spikes/phase-07/claude-code/README.md)) e, ao revisar os riscos residuais do [threat model](22-threat-model.md), o usuário **não aceitou** liberar ferramentas nativas de execução/escrita/rede mesmo com aprovação por chamada; o [ADR-054 foi revisado](../../10-decisoes-arquiteturais.md#revisão-de-25092026-mesma-data-decisão-posterior-do-usuário--riscos-residuais-do-threat-model-não-aceitos-escopo-revertido) para desligar essas ferramentas **por construção** (allowlist exata `--tools`), mantendo só leitura pura (Read/Glob/Grep) do workspace e as tools do produto via MCP. O cancelamento usa só Job Object (Windows)/grupo de processos (Linux). GCL-4 registra, sem aprovar, que a credencial de login fica em `~/.claude/.credentials.json` (não cofre do SO). O bloco substitui o 8B; 7B e lotes 10/11/12 ficam subordinados. [Documento 23](23-integracao-claude.md) · [ADR-053](../../10-decisoes-arquiteturais.md#adr-053--claude-via-assinatura-usando-o-binário-oficial-do-claude-code-como-subprocesso-25092026) · [ADR-054](../../10-decisoes-arquiteturais.md#adr-054--ferramentas-nativas-do-claude-code-com-aprovação-por-chamada-25092026) · [plano](10-plano-de-implementacao.md#bloco-prioritário--integração-claude-25092026). **Nada implementado ou homologado; nenhum AC aprovado.**

## Decisão de escopo de 25/09/2026 (anterior; 8B substituído pela seção acima)

O usuário definiu como **prioridade atual** viabilizar contas próprias **Codex/ChatGPT (7B)** e **Claude (8B)** por runtimes oficiais. Ambos são condicionais, com gates separados de suporte/modalidade, confinamento, credenciais seguras e homologação manual; API Key continua alternativa explícita e não conclui sozinha essa prioridade. O chat completo na UI permanece no lote 6 ampliado. Importação de tokens/sessões não faz parte da integração. [Plano e gates](10-plano-de-implementacao.md) · [Roteiro manual](21-homologacao-manual-login.md). **Os acessos por conta não estão implementados nem homologados; nenhum AC é aprovado por esta decisão.**

## Objetivo e fronteiras

Expor capacidades MongoDB por um Tool Registry único, acessível pelo servidor MCP e pelo Agent Runtime do chat nativo. OpenAI/Codex e Claude são adaptadores opcionais; ONNX continua independente e offline. MCP expõe ferramentas; o runtime governa conversas, eventos, contexto, cancelamento e aprovações. Nenhum provider entra no domínio MongoDB.

Conectar uma conta não autoriza envio de dados. O usuário escolhe destino, contexto e permissões; cada chamada é validada no registry. O primeiro incremento MCP só permite leituras. Escritas só entram após permissões, aprovação vinculada à operação e auditoria durável.

## Roteiro de leitura

| Documento | Conteúdo |
| --- | --- |
| [01 — Requisitos](01-requisitos.md) | Escopo, prioridades, exclusões e rastreabilidade |
| [02 — Arquitetura](02-arquitetura.md) | Dependências, projetos e integração com a base |
| [03 — Agent Runtime](03-agent-runtime.md) | Contratos, eventos, sessões e concorrência |
| [04 — Providers](04-providers.md) | Codex, Claude, local, capabilities e extensão |
| [05 — Servidor MCP](05-mcp-server.md) | Processos, transporte, versões e recuperação |
| [06 — Ferramentas](06-mcp-tools.md) | Implementações existentes, contratos, gaps e risco |
| [07 — Autenticação e segredos](07-autenticacao-e-segredos.md) | Fluxos oficiais e cofre por SO |
| [08 — Permissões e aprovações](08-permissoes-e-aprovacoes.md) | Autorização efetiva e execução protegida |
| [09 — Segurança e privacidade](09-seguranca-e-privacidade.md) | Contexto, saída de dados, auditoria e ameaças |
| [10 — Implementação](10-plano-de-implementacao.md) | Incrementos executáveis e dependências |
| [11 — Testes](11-plano-de-testes.md) | Contratos, falhas, integração e homologação |
| [12 — Aceite](12-criterios-de-aceite.md) | Evidências necessárias para entregar a versão |
| [13 — Análise do código](13-analise-do-codigo.md) | Estado real da solução e lacunas |
| [14 — Migração documental](14-migracao-documental.md) | Preservação e renumeração do roadmap |
| [15 — Fontes e licenças](15-fontes-e-licencas.md) | Pesquisa oficial datada e gates de dependências |
| [16 — Chat nativo](16-chat-nativo.md) | UX, foco, estados e configuração |
| [17 — Validação desta meta](17-validacao-da-meta.md) | Evidência documental e limites da revisão |
| [18 — Persistência de autorização](18-persistencia-de-autorizacao.md) | Persistência versionada de grants no proprietário LiteDB único |
| [19 — Persistência de auditoria](19-persistencia-auditoria.md) | Ledger local versionado, retenção e limites |
| [20 — Agentes e execução](20-agentes-e-execucao.md) | Responsáveis por lote, revisão, handoffs, gates e preparação para Claude Code |
| [21 — Homologação manual do login](21-homologacao-manual-login.md) | Roteiro manual: casos H-01..H-17 (7B) e C-01..C-36 (modo Claude Code, inclui C-35/C-36 de leitura com/sem pasta de workspace), pré-condições e registro (nenhuma execução) |
| [22 — Threat model do modo Claude Code](22-threat-model.md) | Rascunho existente (P7-CL0-02), escrito antes do spike P7-CL0-01; spike executado no Windows, hipóteses H-01..H-25 marcadas; riscos residuais de execução/rede **não aceitos** pelo usuário em 25/09/2026 (escopo revertido); STRIDE por fronteira ainda não revisado linha a linha; gate GCL-3 pendente |
| [23 — Integração Claude](23-integracao-claude.md) | Bloco prioritário: regras, fatos oficiais datados, arquitetura, passos 1–15, gates GCL-1..8 e riscos |

## Sequência e decisões

Fase 6 / v0.10.0 mantém administração. Esta fase cria a fundação de agentes; a [Fase 8 / v0.12.0](../phase-08-v0.12.0/README.md) preserva o chat por workflow integralmente. A homologação já existente passa à [Fase 9 / v0.13.0](../phase-09-v0.13.0/README.md); estabilidade passa à [Fase 10 / v1.0.0](../phase-10-v1.0.0/README.md). Os testes reais necessários para aceitar v0.11.0 continuam obrigatórios nesta fase; a fase de homologação posterior amplia a matriz e não dispensa os gates de segurança.

As [ADR-046 a ADR-051](../../10-decisoes-arquiteturais.md#adr-046--runtime-e-adaptadores-de-agentes-22092026) registram decisões propostas para implementação. A execução continua com os gates abertos do lote 0 e os recortes parciais dos lotes 1–2; pacote, protocolo e termos devem ser revalidados antes de incorporar dependências. Nenhum modelo, SDK ou recurso comercial foi integrado ao produto por este planejamento.
