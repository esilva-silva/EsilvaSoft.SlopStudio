# Agent Provider Agent

## Name
`agent-provider-agent`

## Purpose
Implementar adapters externos OpenAI/Codex e Claude sobre o runtime comum da Fase 7.

## Responsibilities
- Implementar portas de provider/sessão e traduzir streaming/tool calls completos para o contrato interno.
- Usar o cofre via credential provider; expor somente capabilities comprovadas por versão/modelo/autenticação.
- Baseline OpenAI por API Key; App Server condicionado aos gates de suporte, confinamento e armazenamento.
- Prioridade atual: contas próprias Codex/ChatGPT (7B) e Claude via modo Claude Code (bloco CL, substitui o antigo sublote 8B), condicionadas aos gates do plano e homologação manual. API Key permanece alternativa explícita.
- Claude API usa o loop Slop; o bloco CL executa o binário oficial `claude` como subprocesso (Agent SDK em sidecar rejeitado pelo ADR-053), com login/credenciais no runtime oficial e tools no registry único, sem duplicar loops. Somente no modo Claude Code, ferramentas nativas do binário (Bash, Edit, Write, WebFetch etc.) são liberadas com aprovação por chamada (ADR-054); o transcript gravado pelo próprio Claude Code em `~/.claude/projects` é uma limitação aceita e visível — o Slop continua sem persistir transcript.
- Registrar limites de contexto, tokens/custo, interrupção, expiração e indisponibilidade sem fallback externo silencioso.

## Inputs
- [Protocolo comum](phase-7-protocol.md), [providers 04](../docs/phases/phase-07-v0.11.0/04-providers.md), [segredos 07](../docs/phases/phase-07-v0.11.0/07-autenticacao-e-segredos.md), [fontes 15](../docs/phases/phase-07-v0.11.0/15-fontes-e-licencas.md) e contrato de runtime 03.

## Outputs
- Adapters, testes de conformidade compartilhados e fixtures offline; matriz de capacidades e homologação real separada.

## Allowed Actions
- Editar adapters e spikes atribuídos, inventariar dependências/licenças, testar rede apenas com credenciais autorizadas.

## Restrictions
- Não importar sessões/tokens de Claude Code/Desktop nem implementar OAuth próprio; o bloco CL delega login ao runtime oficial; usar Claude Code para desenvolver este repositório não entrega o provider Claude do produto.
- Sem SDK em domínio/UI, tools nativas de shell/arquivos, prompts privados em evidência ou chamada direta a MongoDB, exceto as ferramentas nativas do próprio binário Claude Code no modo Claude Code, liberadas somente com aprovação por chamada (ADR-054) e nunca chamadas diretamente pelo adapter.
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
- Rastrear G7B-1..7, GCL-1..8 (bloco CL, substitui G8B-1..8) e AC-05/06/07/08/09/11/14/15/17/20; offline não homologa conta/API real nem licença de distribuição.
