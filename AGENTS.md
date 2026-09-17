# Regras de desenvolvimento

## Referências e escopo

- Leia `docs/17-design-system-ui-ux.md` antes de alterar UI, navegação, temas, sessão ou atalhos.
- Consulte o catálogo funcional, os ADRs e a matriz de validação para distinguir implementado de planejado.
- Produto: IDE desktop MongoDB em .NET 10/Avalonia; Windows e Linux; interface e documentação em pt-BR, identificadores em inglês.
- Mantenha o nome EsilvaSoft.SlopStudio e a licença MIT. Não transformar o desktop em site ou adicionar dependência comercial sem pedido específico.

## Responsabilidades e agentes especializados

- O diretório central `/agents/` concentra todos os agentes especializados da solução e sua orquestração por metas (`agents/goal-orchestrator.md`).
- A abstração de modelos e regras de escalonamento residem em `agents/capabilities.md`.
- UI/UX: hierarquia, tokens, tipografia, teclado, foco, estados vazios/erro e evidência visual nos dois temas (`agents/ui-ux-agent.md`).
- Arquitetura: contexto fixo por aba, snapshots antes de awaits, cancelamento isolado, contratos explícitos e persistência versionada (`agents/architecture-agent.md`).
- Qualidade: cenários observáveis, fixtures independentes, integridade BSON, concorrência, privacidade e distinção entre teste automatizado e homologação real (`agents/qa-testing-agent.md`, `agents/code-review-agent.md`).
- Esses papéis são regras reutilizáveis do repositório; consulte `agents/README.md` para o catálogo completo.

## Invariantes

- Explorer navega; nunca executa automaticamente uma consulta ao selecionar ou abrir coleção.
- Mudar seleção do explorer não redireciona abas abertas. Um resultado só atualiza a aba/solicitação que o originou.
- Capturar perfil, banco, coleção, texto e opções antes de iniciar operações assíncronas.
- Não compartilhar CancellationTokenSource entre abas. Não afirmar rollback ao cancelar.
- Não abrir uma segunda conexão LiteDB ao arquivo local: use o proprietário registrado em DI e migração aditiva/versionada.
- Rascunhos respeitam opt-out geral/por conexão; entrada JSON respeita opt-in. Não persistir resultados ou credenciais nos snapshots.
- Falha de persistência deve ser visível. Nunca sobrescrever sessão ilegível com uma sessão vazia.
- Preservar BSON/Extended JSON, UUIDs, proteções de escrita, confirmações, auditoria e limites do runtime mongosh.

## Verificação e documentação

- Restore: `dotnet restore EsilvaSoft.SlopStudio.slnx --locked-mode`.
- Build: `dotnet build EsilvaSoft.SlopStudio.slnx --no-restore`.
- Testes: `dotnet test EsilvaSoft.SlopStudio.slnx --no-build --no-restore`.
- Em ambientes isolados que bloqueiam a telemetria de build do Avalonia, `-p:UsedAvaloniaProducts=` permite validar sem essa tarefa externa; não desabilita analisadores nem testes.
- Alterações de sessão/contexto exigem testes de falha, concorrência e recuperação. Alterações visuais exigem inspeção dos PNGs reais gerados pelos testes de renderização.
- Não alterar golden files ou asserções apenas para esconder regressões. Não criar testes que apenas repitam a implementação.
- Atualize design system, ADRs, plano, guia e acompanhamento quando decisões ou comportamento mudarem.
- Declare conclusão somente com evidência proporcional. Teste Headless não substitui MongoDB real, leitor de tela ou diálogos nativos. Registre pendências com precisão.
