# Requisitos da v0.11.0

**Estado: planejamento.** As capacidades atuais estão na [análise da solução](13-analise-do-codigo.md); as linhas abaixo não são declaração de implementação.

| ID | Requisito e prioridade | Contrato/evidência de aceite |
| --- | --- | --- |
| AGT-01 | Obrigatório: runtime independente de provider, UI nativa e eventos normalizados | [Runtime](03-agent-runtime.md); AC-04/09/10/11 |
| AGT-02 | Obrigatório: adaptador Codex, login ChatGPT oficial e API Key do usuário | [Providers](04-providers.md), [autenticação](07-autenticacao-e-segredos.md); AC-05/07 |
| AGT-03 | Obrigatório: adaptador Claude por API Key, streaming, contexto e tools | Mesmos contratos; AC-06/07; login só se oficialmente autorizado |
| AGT-04 | Obrigatório: ONNX independente; capabilities refletem o modelo carregado | AC-16; nenhuma chamada externa como fallback |
| AGT-05 | Obrigatório: registro único de tools para MCP e runtime | [Catálogo](06-mcp-tools.md); AC-01/02/14 |
| AGT-06 | Obrigatório: conexão lógica, BSON fiel e leitura com limites | AC-03/13; credenciais resolvidas somente localmente |
| AGT-07 | Obrigatório: permissões determinísticas e aprovação antes de efeitos | [Política](08-permissoes-e-aprovacoes.md); AC-12 |
| AGT-08 | Obrigatório: cofre por SO e falha visível sem plaintext | AC-08/17; bloqueia persistência de credenciais |
| AGT-09 | Obrigatório: saída de dados explícita, auditável e limitada | [Privacidade](09-seguranca-e-privacidade.md); AC-03/13 |
| AGT-10 | Obrigatório: funcionamento degradado e cancelamento isolado | AC-11/15/16/17 |
| AGT-11 | Obrigatório: migração preserva workflow e homologação | AC-18; comparação documental |
| AGT-12 | Incremento posterior ao MCP read-only: insert/update/delete e índices protegidos | AC-12/19; nenhuma aprovação genérica |
| AGT-13 | Extensível: novo provider sem branch de fornecedor na UI | AC-09/20; Copilot futuro, sem promessa nesta versão |

## Escopo do primeiro MCP

Listar conexões autorizadas, bancos e coleções, obter schema, amostrar documentos, find, count, distinct, explain e índices. Cada tool só será exposta após o gap correspondente no catálogo ser encerrado. Leitura de metadados e leitura de documentos são permissões diferentes. Mesmo uma contagem pode revelar informação; o rótulo READ_ONLY não concede acesso.

O escopo completo prevê integração dos dois providers, chat e credenciais seguras. O marco read-only é um incremento verificável, não o aceite automático de toda a versão. Tools administrativas e execução arbitrária de scripts/terminal permanecem desabilitadas; operações administrativas só entram em lote posterior com especificação própria. Streamable HTTP é extensão planejada, fora do gate mínimo STDIO; o contrato já define os requisitos para habilitá-lo.

## Exclusões e compatibilidade

Não criar um site, WebView de ChatGPT/Claude, créditos fornecidos pela aplicação, login por cookies, acesso implícito a arquivos/segredos, shell livre, editor de workflows nesta fase ou migração destrutiva do workspace. O workflow controlado continua na v0.12.0 com seu escopo preservado.

Capturar perfil, revisão, banco, coleção, texto, opções e permissões antes de awaits. Explorer apenas navega. Abas existentes nunca seguem seleção do Explorer. Cada sessão/turno tem seu próprio cancelamento e correlação. Cancelamento não significa rollback. LiteDB continua com proprietário único registrado em DI; rascunhos preservam opt-out, entrada JSON opt-in e exclusão de resultados e credenciais. Uma sessão ilegível não pode ser sobrescrita.

## Rastreabilidade do pedido original

Os itens 1/27/34 do pedido são cobertos pela migração e índice; 2–7/14/18/24/25 por arquitetura/runtime/providers; 8–15/30 por servidor/catalogação/políticas; 16–17 pela UX; 19–23 por autenticação/privacidade; 26 pelas fontes/licenças; 28 pelas seis ADRs; 29 pelo plano; 31–33 por testes/aceite/degradação. A revisão externa da especificação e licenças é datada, não uma garantia permanente de compatibilidade.
