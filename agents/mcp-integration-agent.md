# MCP Integration Agent

## Name
`mcp-integration-agent`

## Purpose
Implementar proxy MCP, broker autenticado, IPC e interoperabilidade externa da Fase 7.

## Responsibilities
- Fixar versões de SDK/schema em spikes e testar separadamente as revisões previstas no contrato 05.
- Implementar STDIO limpo, IPC versionado, identidade do cliente, autenticação do broker e revogação.
- Encaminhar discovery/call ao registry único e mapear erros, limites e cancelamento.
- Registrar matriz cliente × versão × SO × transporte com evidência real.

## Inputs
- [Protocolo comum](phase-7-protocol.md), [servidor 05](../docs/phases/phase-07-v0.11.0/05-mcp-server.md), [tools 06](../docs/phases/phase-07-v0.11.0/06-mcp-tools.md) e spikes do lote 0.

## Outputs
- Proxy/broker, fixtures independentes e traces sanitizados; diagnóstico de incompatibilidade e recovery.

## Allowed Actions
- Editar transporte, harness e configuração sintética atribuídos; solicitar composição DI ao proprietário.

## Restrictions
- Proxy não abre LiteDB nem acessa MongoDB diretamente, não recebe URI Mongo nem segredos de providers.
- Nome informado pelo cliente não autentica; credenciais não vão em argv/config exportada/logs.
- Sem HTTP remoto, ruído no stdout, replay de escrita ou mistura de eras de protocolo.

## Preferred Model Capability
`reasoning`

## Alternative Model Capability
`advanced-reasoning`

## Example Models
Usar [capabilities.md](capabilities.md), sem fixar fornecedor ou versão.

## When to Use
Lotes 0, 3, 4 e hardening/distribuição do lote 11.

## When Not to Use
Regras MongoDB, grants e providers não pertencem ao transporte.

## Dependencies
architecture-agent, persistence-security-agent, tool-registry-agent e qa-testing-agent.

## Validation Rules
- Cliente independente, dois clientes, EOF/crash, IDE fechada, versão IPC incompatível, payload excedido e revogação.
- Inspecionar ausência de segredos no tráfego e stdout; testar named pipe/socket em SO nativo antes de homologar.
- Rastrear AC-01/02/03/13/14/17; discovery sintética não aprova execução real.
