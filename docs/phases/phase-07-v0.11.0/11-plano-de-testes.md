# Estratégia de testes da v0.11.0

**Planejada, não executada nesta meta.** A suíte atual valida a base existente; não prova uma integração ainda não implementada. Fixtures devem ser independentes, com dados sintéticos e teardown de arquivos, processos, CTS e proprietário LiteDB.

| Família / ID | Cenário observável | Evidência exigida |
| --- | --- | --- |
| RT-01 | Sessão, mensagem/deltas/terminal, tool-result sem deadlock, erro tipado | Testes de contrato comuns a todos adapters; relógio controlado, eventos correlacionados |
| RT-02 | A e B simultâneas; cancelar A, mudar Explorer e receber resposta antiga | B preservada; nenhuma mudança na aba errada; um terminal por chamada |
| RT-03 | Fila cheia, consumidor parado, timeout, provider morto e stream infinito | Limites de memória/prazo; erro recuperável sem UI bloqueada |
| RT-04 | Retomar/reconectar, tool duplicada, request ID com outros argumentos | Nenhuma escrita repetida; conflito ou estado conhecido; crash após envio incerto |
| PR-01 | Adapter traduz mensagens fragmentadas/argumentos parciais/eventos desconhecidos | Fixtures do protocolo real versionadas, sem secrets; nunca executar tool parcial |
| PR-02 | Provider indisponível, modelo removido, cota/billing, chave inválida | Diagnóstico acionável; sem loop, fallback pago ou envio a outro provider |
| AU-01 | Login cancelado, timeout, callback tardio, conta substituída | Estado correto da conta; callback antigo descartado |
| AU-02 | Token expirado, refresh concorrente e logout falho | Uma renovação por conta, limite de retries, falha visível |
| SK-01 | Cofre Windows/Linux ausente/bloqueado/negado e memória explícita | Sem auth.json/LiteDB/arquivo plaintext; IDE continua funcional |
| SK-02 | Queda após cada etapa da migração, segredo órfão, sessão ilegível | Recuperação sem perder referência válida nem substituir banco por vazio |
| MCP-01 | Descoberta/call e versões suportadas/não suportadas | Fixtures distintas 2026-07-28 e 2025-11-25; schema, direção e cancelamento corretos |
| MCP-02 | JSON inválido, schema inválido, tool inexistente, campo extra e payload gigante | Erro sanitizado; nenhuma chamada MongoDB |
| MCP-03 | STDIO limpo, EOF, IDE fechada, proxy incompatível, cliente forjado | stdout só protocolo; sem segundo LiteDB; negar acesso indevido |
| TOOL-01 | Cada tool por MCP e internamente com mesmos dados/grants | Mesmos resultados/negações; um handler; input/output schema fechado |
| TOOL-02 | BSON ObjectId/Date/Int64/Decimal128/Binary/regex e UUID quatro representações | Roundtrip independente com bytes/tipos, sem perda por número JSON |
| TOOL-03 | ENV literal, JS/constructors, `$where`/`$function`, aggregate com escrita aninhada | Negação antes de resolver segredo ou despachar ao banco |
| TOOL-04 | `$lookup`/`$unionWith` negados, views e namespace alterado | Verificar destino real autorizado; sem acesso lateral |
| TOOL-05 | Página grande, documento grande, distinct caro, explain executionStats | Limites de bytes/deadline/custo e truncamento entre documentos íntegros |
| PER-01 | Sem grant, read-only, política revogada, outra conexão/cliente/cursor | Deny vence; ausência de vazamento mesmo via erro |
| APR-01 | Rejeitar, expirar, fechar UI, cancelar, duplicar aprovação | Nenhum efeito; ticket único; nenhuma aprovação por texto do modelo |
| APR-02 | Trocar alvo/argumento/revisão após consentimento | Nova aprovação exigida; não enviar proposta diferente |
| WR-01 | Insert/update/delete concorrentes e precondição inválida | Uma operação unitária, sem upsert/multi implícito; conflito observável |
| WR-02 | Drop `_id_`, índice alterado, falha RBAC, crash após commit | Proteções mantidas, sem replay; resultado incerto quando aplicável |
| AUD-01 | Auditoria falha antes/depois do despacho | Antes: negar; depois: conservar intenção e indicar falha sem repetir escrita |
| PRIV-01 | Canários em senha/URI/env/token/prompt/exceção | Ausentes em tráfego não autorizado, snapshots, logs, screenshots e crash reports |
| PRIV-02 | Conectar provider; trocar provider; tool tenta ampliar contexto | Zero dados MongoDB no primeiro caso, consentimento no segundo, negação no terceiro |
| UI-01 | Chat vazio, streaming, tool, aprovação, offline, auth falha, texto longo | PNGs reais claro/escuro em 960×620, 1366×768, 1920×1080; 100/150/200%; inspecionar clipping e foco |
| UI-02 | Tab/Shift+Tab/Escape, retorno de foco, contraste, anúncio de status | Headless para comportamento e homologação nativa com leitor de tela separada |
| DEG-01 | Sem internet/OpenAI/Claude/modelo local/MCP e falha simultânea | Consultas manuais, arquivos e autocomplete determinístico utilizáveis |

## Integração real

MongoDB: laboratório descartável com dados sintéticos, usuário somente leitura e usuário de escrita, versões/topologias já suportadas pelo produto. Validar precondições e efeitos no servidor, não apenas contagem de chamadas do mock. Medir explain/aggregate e interrupção; não executar testes destrutivos em conexão de produção.

Providers: rotular explicitamente testes dependentes de credenciais, modelo, conta, custo e disponibilidade. Executar em ambiente autorizado com segredo efêmero/cofre, sem API Keys na CI, fixtures ou argumentos. Registrar apenas versões, cenário, resultado e evidência sanitizada. Testar Codex login/API Key e Claude API Key; login Claude ausente da UI faz parte do aceite. Não considerar mocks equivalentes a login ou billing reais.

Clientes MCP: usar cliente de referência e, depois, Codex e Claude Code/Desktop nas versões homologadas. Testar descobrir ferramentas, autorizar conexão, executar leitura, negar escrita, revogar grant e encerrar host. Copilot permanece compatibilidade futura até evidência. Matrizes identificam SO, revisão MCP, versão do SDK/cliente/proxy, cenário e resultado; não escrever simplesmente “MCP compatível”.

Windows/Linux nativos: cofre/IPC, instalação proxy, atualização conjunta, crash recovery, diálogos, teclado, leitor de tela e offline. Headless e CI em um SO não provam suporte no outro. Não transferir falhas essenciais de v0.11.0 para a fase de homologação geral como se estivessem aceitas.

## Organização e execução

Começar em UnitTests com fixtures por contrato; extrair harness/projeto IntegrationTests apenas quando houver necessidade operacional concreta. Tests de arquitetura proíbem SDK/driver/Avalonia em Core/Application e dependência de LiteDB no proxy. Benchmarks medem fila/serialização e impacto no editor com volume definido; não criar testes que apenas espelham getters ou implementação.

Comandos oficiais: `dotnet restore EsilvaSoft.SlopStudio.slnx --locked-mode`, `dotnet build EsilvaSoft.SlopStudio.slnx --no-restore`, `dotnet test EsilvaSoft.SlopStudio.slnx --no-build --no-restore`. Em ambiente com telemetria Avalonia bloqueada, usar `-p:UsedAvaloniaProducts=` no build, preservando analisadores/testes. Golden files não podem ser atualizados para esconder regressão.
