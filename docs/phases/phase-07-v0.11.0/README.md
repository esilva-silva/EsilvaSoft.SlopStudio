# Fase 7 — v0.11.0: MCP e integração com agentes externos

**Situação: planejada; nenhuma integração desta fase implementada por esta meta.** Plano revisado em 22/09/2026 para o EsilvaSoft.SlopStudio (.NET 10/Avalonia, Windows/Linux, MIT).

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

## Sequência e decisões

Fase 6 / v0.10.0 mantém administração. Esta fase cria a fundação de agentes; a [Fase 8 / v0.12.0](../phase-08-v0.12.0/README.md) preserva o chat por workflow integralmente. A homologação já existente passa à [Fase 9 / v0.13.0](../phase-09-v0.13.0/README.md); estabilidade passa à [Fase 10 / v1.0.0](../phase-10-v1.0.0/README.md). Os testes reais necessários para aceitar v0.11.0 continuam obrigatórios nesta fase; a fase de homologação posterior amplia a matriz e não dispensa os gates de segurança.

As [ADR-046 a ADR-051](../../10-decisoes-arquiteturais.md#adr-046--runtime-e-adaptadores-de-agentes-22092026) registram decisões propostas para implementação. A execução futura começa pelo lote 0 do plano; pacote, protocolo e termos devem ser revalidados antes de incorporar dependências. Nenhum modelo, SDK ou recurso comercial é instalado por este planejamento.
