# EsilvaSoft.SlopStudio — desenvolvimento com Claude Code

@AGENTS.md

Responda e documente em pt-BR; identificadores de código em inglês. Antes de editar um diretório, leia seu `AGENTS.md` local quando existir.

Para metas com múltiplos componentes, a conversa principal segue `agents/goal-orchestrator.md`. Leia `agents/README.md` e delegue recortes aos especialistas de `.claude/agents`; seus contratos completos ficam em `agents/`. Não presumir contexto herdado nem delegação recursiva. Sem delegação disponível, execute os papéis em sequência e registre essa limitação.

Para a Fase 7, leia `agents/phase-7-protocol.md`, `docs/phases/phase-07-v0.11.0/10-plano-de-implementacao.md` e `20-agentes-e-execucao.md` na mesma pasta. Confira `docs/memory/phase-7.md` contra código e git status antes de retomar; evidência histórica não é validação atual.

Os especialistas de desenvolvimento não habilitam subagentes do produto. Preparar Claude Code não implementa o provider Claude nem autoriza usar credenciais pessoais. Preserve as permissões da sessão e as ferramentas disponíveis; não configure bypass de permissões. Instruções detalhadas e prompt de retomada em `agents/claude-code.md`.
