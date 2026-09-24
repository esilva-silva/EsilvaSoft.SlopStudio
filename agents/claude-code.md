# Uso dos agentes no Claude Code

## Estrutura

`CLAUDE.md` importa `AGENTS.md`. Cada adapter em `.claude/agents/` manda ler o contrato canônico em `agents/`; as regras não são copiadas. O coordenador atua na conversa principal, sem adapter próprio que dependa de delegação recursiva. O formato usa Markdown com frontmatter `name`, `description`, `model` e `permissionMode`, conforme a [documentação oficial de subagents](https://code.claude.com/docs/en/sub-agents), consultada em 24/09/2026.

Os executores herdam as ferramentas disponíveis; o revisor recebe somente `Read`, `Glob` e `Grep`. Todos usam `model: inherit` e `permissionMode: default`; capacidade é orientação de risco, não seletor automático de modelo. Não há hooks, login, permissão bypass ou configuração MCP acrescentados. Importação de instruções segue a [documentação de memória](https://code.claude.com/docs/en/memory).

Para revisão, a sessão principal/QA fornece diff sanitizado (incluindo arquivos novos), revisão base/atual e logs com comandos/exit codes em arquivos legíveis ou no despacho. O revisor lê o código e essas evidências; não depende de shell e não relata testes recebidos como se os tivesse executado.

## Retomada

Inicie uma sessão Claude Code na raiz do checkout. Confira as instruções carregadas com `/memory`. Peça explicitamente o especialista desejado; a sessão principal permanece responsável por integração, comandos de validação e aceite. Use, por exemplo:

```text
Continue a implementação da Fase 7 do EsilvaSoft.SlopStudio.
Na sessão principal, siga agents/goal-orchestrator.md e agents/phase-7-protocol.md.
Leia o plano 10, a matriz 20 e os critérios 12 em docs/phases/phase-07-v0.11.0/.
Confronte docs/memory/phase-7.md com o código e git status antes de escolher o incremento.
Selecione o próximo recorte cujas dependências permitem execução; não refaça entregas existentes.
Delegue aos especialistas indicados na matriz, com arquivos exclusivos, ACs e cenários de falha.
Integre em série, valide e registre evidências/pendências; não exponha tools antes dos gates.
```

Para verificar descoberta sem implementar, peça: “Use o architecture-agent apenas para ler seu contrato e resumir as dependências do próximo lote, sem editar nem chamar rede”. Confira nome, modelo e arquivos lidos no retorno. Se o agente não for encontrado, reinicie a sessão e confira escopo e permissões do projeto. Registre a versão local com `claude --version` antes de relatar compatibilidade; este repositório não fixa uma versão mínima homologada.

## Manutenção e verificação

```text
node scripts/sync-claude-agents.cjs
node scripts/sync-claude-agents.cjs --check
node scripts/build-docs-index.cjs
```

O gerador descobre perfis canônicos por `## Name` e deriva a descrição de `## Purpose`; valide ambos ao criar um perfil. `--check` não escreve: detecta adapter ausente, divergente ou órfão e contrato incompleto. O coordenador é excluído deliberadamente. Não editar adapters gerados à mão. Mudanças específicas de ferramentas são centralizadas no gerador.

Esta verificação cobre arquivos, não instalação, autenticação ou execução real no Claude Code. Não usar a conta da ferramenta de desenvolvimento como credencial do provider do SlopStudio. O lote 8 e AC-06 permanecem sujeitos aos seus próprios testes.
