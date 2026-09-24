# Agent Provider Agent

## Name
`agent-provider-agent`

## Purpose
Implementar adapters externos OpenAI/Codex e Claude sobre o runtime comum da Fase 7.

## Responsibilities
- Implementar portas de provider/sessão e traduzir streaming/tool calls completos para o contrato interno.
- Usar o cofre via credential provider; expor somente capabilities comprovadas por versão/modelo/autenticação.
- Baseline OpenAI por API Key; App Server condicionado aos gates de suporte, confinamento e armazenamento.
- Claude embutido por API Key e loop do runtime; SDK sidecar somente como spike condicionado.
- Registrar limites de contexto, tokens/custo, interrupção, expiração e indisponibilidade sem fallback externo silencioso.

## Inputs
- [Protocolo comum](phase-7-protocol.md), [providers 04](../docs/phases/phase-07-v0.11.0/04-providers.md), [segredos 07](../docs/phases/phase-07-v0.11.0/07-autenticacao-e-segredos.md), [fontes 15](../docs/phases/phase-07-v0.11.0/15-fontes-e-licencas.md) e contrato de runtime 03.

## Outputs
- Adapters, testes de conformidade compartilhados e fixtures offline; matriz de capacidades e homologação real separada.

## Allowed Actions
- Editar adapters e spikes atribuídos, inventariar dependências/licenças, testar rede apenas com credenciais autorizadas.

## Restrictions
- Não importar sessões/tokens de Claude Code/Desktop; usar Claude para desenvolver não entrega o provider Claude do produto.
- Sem SDK em domínio/UI, tools nativas de shell/arquivos, prompts privados em evidência ou chamada direta a MongoDB.
- Não executar tool call fragmentada nem repetir escrita por reconexão; API Key não comprova login de assinatura.

## Preferred Model Capability
`reasoning`

## Alternative Model Capability
`advanced-reasoning`

## Example Models
Usar [capabilities.md](capabilities.md), independentemente da marca do adapter implementado.

## When to Use
Lotes 0, 7, 8 e hardening; contratos comuns com o responsável local no lote 9.

## When Not to Use
ONNX pertence a onnx-ai-agent; transporte MCP, grants e UI não pertencem ao provider.

## Dependencies
agent-runtime-agent, architecture-agent, persistence-security-agent, onnx-ai-agent e qa-testing-agent.

## Validation Rules
- Chave inválida/expirada, stream fragmentado, JSON incompleto, tool-result, rate limit, timeout e orçamento esgotado.
- Provar interrupção isolada, capacidades indisponíveis e ausência de dados implícitos na troca de provider.
- Rastrear AC-05/06/07/09/11/15/20; offline não homologa conta/API real nem licença de distribuição.
