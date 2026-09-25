# Permissões e aprovações

**Proposta — ADR-048/050.** O `IAgentToolRegistry` é a única porta de execução. Descrições de tools, prompt do sistema, MCP annotations, aprovação do cliente externo e permissões do provider não concedem acesso ao MongoDB.

## Decisão efetiva

Permitir somente a interseção: política global ∩ perfil/conexão ∩ principal autenticado ∩ sessão/turno ∩ namespace ∩ operação ∩ escopo de saída de dados. Qualquer negação vence. Conexão read-only nega toda escrita, inclusive índice e administração; a permissão MongoDB do usuário continua sendo a última barreira, não um substituto das anteriores.

`AgentPrincipal` é criado por runtime/broker autenticado, nunca desserializado de argumentos do modelo. Contém origem interna/externa, provider opcional, client/session IDs e revisão de concessões. Contas/modelos são atributos de diagnóstico, não identidade de autorização. Políticas desconhecidas, falha de leitura ou sessão ilegível resultam em negação visível.

O contrato interno atual tipa cada concessão como `ForSession(sessionId)` ou `ForTurn(sessionId, turnId)`. Ambas vinculam principal, destino de saída tipado, classe de dados de saída, conexão/namespace, permissão e `ConnectionProfile.SourceGenerationId`. `ForSession` pode cobrir múltiplos turnos apenas daquela mesma sessão; `ForTurn` exige correspondência exata de sessão e turno. Sessão ou turno ausente quando exigido, geração nula/vazia/divergente, destino sem concessão explícita ou escopo de dados diferente nega. `Local` e `ProviderExternal(providerId)` são destinos distintos; saída para provider exige concessão própria para aquele destino e o providerId recebido pelo runtime só identifica o roteamento, nunca o principal. Trocar origem do perfil renova `SourceGenerationId` e invalida concessões antigas; não se deriva hash de URI ou credencial. Não há curinga implícito de sessão, turno ou destino.

A [persistência interna de concessões](18-persistencia-de-autorizacao.md) usa coleção aditiva versão 1 no proprietário LiteDB existente, comparação de revisão em toda escrita e preservação de documentos ilegíveis. Provider/repository e evaluator já estão registrados no DI, compartilhando o proprietário único; a UI e integração com runtime/MCP continuam pendentes. O [ledger de auditoria](19-persistencia-auditoria.md) também está persistido/composto, mas ainda não ligado ao registry; chamadas não devem ser expostas até o pipeline fechar em falha de auditoria. A ausência de política nega; uma lista vazia explícita revoga com incremento de revisão.

| Permissão proposta | Padrão de cliente recém-registrado | Escopo |
| --- | --- | --- |
| `ReadMetadata` | Negado até escolher conexões | Nomes, propriedades e índices; sem URI/host sensível |
| `ReadSchema` | Negado | Schema autorizado sem valores; amostragem exige autorização adicional |
| `ReadDiagnostics` | Negado | Planos/estatísticas saneados; consulta executada por explain exige também grants de consulta/dados |
| `ExecuteReadQueries` | Negado | Consultas limitadas em namespaces concedidos |
| `ReadDocuments` | Negado | Valores completos ou projeção autorizada |
| `InsertDocuments` / `UpdateDocuments` | Negado | Escrita unitária com aprovação |
| `DeleteDocuments` | Negado | Exclusão unitária com confirmação destrutiva |
| `CreateIndexes` / `DropIndexes` | Negado | DDL com aprovação e proteção `_id_` |
| `AdministrativeOperations` | Negado; fora do primeiro escopo | Lista explícita por operação, nunca `runCommand` livre |

O assistente de configuração pode oferecer preset de metadados; só o gesto de salvar concede acesso. Autorizar conexão não autoriza todo banco futuro: wildcard exige escolha explícita e explicação. Revogar cancela pendências e invalida cursores/approvals. O serviço reavalia revisão de política antes de enviar ao MongoDB e antes de liberar saída ao cliente.

## Pipeline verificável

1. Validar descriptor/version, schema e limites; rejeitar tool desconhecida, propriedades extras de controle e Extended JSON não literal.
2. Resolver conexão lógica e snapshot de revisão sem revelar segredos. Fixar banco/coleção/opções e UUID representation.
3. Autorizar principal, operação e contexto de saída. Classificar risco pelo descriptor e parâmetros; um pipeline que escreve não pode receber risco READ_ONLY.
4. Construir proposta imutável; registrar intenção de auditoria sanitizada. Obter aprovação se requerida.
5. Revalidar destino, política e proposta aprovada; consumir aprovação de uso único atomicamente.
6. Executar o handler compartilhado com token próprio, limite e deadline.
7. Persistir resultado de auditoria; redigir/limitar saída; publicar eventos ao originador. Falha após envio gera `OutcomeUnknown` quando não é possível comprovar resultado.

## Aprovação vinculada à proposta

`ApprovalRequest` identifica principal, sessão, turno, chamada, tool/version, revisão de perfil/política, namespace, risco, resumo exibível, hash canônico de argumentos e validade. Hash não é autorização nem deve persistir valores de consulta de baixa entropia. O ticket de uso único vive no broker e não é um parâmetro controlado pelo modelo.

UI mostra destino fixo, filtro/identidade, diff BSON proposto, quantidade máxima atingida e consequência. Para update/delete unitário exigir `_id` inequívoco e estado original ou token de concorrência; erro de pré-condição não pode virar upsert ou updateMany. Reutilizar controles de conflito atuais e retornar conflito observável. Conexões com resolução dinâmica devem fixar identidade efetiva local e invalidar consentimento se a resolução mudar; não substituir valores após a aprovação.

`Rejeitar` e `Aprovar uma vez` são as ações iniciais. Não há `Aprovar sempre` para escrita, exclusão, índices ou administração. Leitura pode usar `ForSession(sessionId)` configurada separadamente, limitada a namespace/escopo/deadline e reutilizável somente entre turnos dessa sessão; uma concessão `ForTurn(sessionId, turnId)` não se estende a outro turno. Escape/fechar/reiniciar/cancelar/timeout negam. Um segundo evento com o mesmo approval ID é ignorado; duas requisições concorrentes não consomem o mesmo ticket. Alterar argumento, provider, conexão, usuário MongoDB ou revisão exige nova proposta.

Antes de executar, persistir intenção de escrita com identificador de operação. Se auditoria não grava, não executar. Se o banco já confirmou escrita e gravar desfecho falha, exibir a falha e manter intenção pendente para reconciliação, sem repetir a escrita. Não esconder o sucesso do banco nem afirmar rollback. Reconciliação usa identificação específica e leitura autorizada, não replay automático.

## Riscos e controles adicionais

READ_ONLY limita efeitos de escrita, mas pode expor dados e causar carga: deadline/bytes/rate limit e escopo de privacidade permanecem. WRITE sempre pede aprovação; DESTRUCTIVE exige confirmação explícita e nome do destino. ADMINISTRATIVE fica bloqueado até catálogo específico e homologação RBAC. `$out`, `$merge`, execução JS, `mapReduce`, `$where`, `$function`, comandos arbitrários e resoluções `ENV` não entram nas tools de leitura. Lookup/union e pipelines aninhados validam todos os namespaces; se o parser não prova a autorização, negar.

As mesmas regras valem para tool interna, MCP, adapter OpenAI e adapter Claude. Tools nativas de arquivo/shell dos providers ficam desativadas no primeiro escopo; ligar um MCP protegido e deixar um terminal com acesso aos segredos seria uma rota de contorno inaceitável. **Exceção decidida pelo usuário em 25/09/2026, somente no modo Claude Code**, regida pela seção seguinte e bloqueada até o threat model e o gate de segurança.

## Ferramentas nativas do Claude Code — aprovação por chamada (ADR-054)

**Planejado, não implementado.** Mudança de escopo decidida pelo usuário ([ADR-054](../../10-decisoes-arquiteturais.md#adr-054--ferramentas-nativas-do-claude-code-com-aprovação-por-chamada-25092026)); vale apenas para `ClaudeCodeAgentProvider`. As tools do produto (registry/MCP Mongo) mantêm todas as regras acima, inclusive aprovação única sem "sempre" para escrita.

| Categoria | Exemplos de ferramenta da CLI | Padrão | "Sempre permitir nesta sessão" |
| --- | --- | --- | --- |
| `ReadFile` | Read, Grep, Glob | Pedir aprovação quando a CLI consultar | Permitido, por ferramenta e escopo de caminho |
| `WriteFile` | Edit, Write | Pedir aprovação | Permitido apenas dentro do cwd; sobrescrita fora dele é destrutiva |
| `DeleteFile` | exclusões por comando ou ferramenta | **Destrutiva**: sempre pedir | Só após confirmação específica do alvo |
| `ExecuteCommand` | Bash | Pedir aprovação; comando não classificável como seguro é destrutivo | Comando exato normalizado; destrutivo só com confirmação específica |
| `Network` | WebFetch, WebSearch | Pedir aprovação, mostrando domínio | Por domínio |
| `MCP` | tools MCP que não sejam do McpServer Slop | Negadas por `--strict-mcp-config`; se aparecerem, pedir | Não |
| `ExternalTool` | outras ferramentas nativas conhecidas | Pedir aprovação | Por ferramenta |
| (sem categoria) | ferramenta desconhecida, subagentes nativos | **Negar** | Não |

Regras:

- O pedido chega por `--permission-prompt-tool` a uma tool MCP do McpServer/broker Slop, vinculada ao canal autenticado da sessão do chat; o diálogo mostra "Claude deseja executar: <comando/alvo>", categoria, cwd e risco, com **Permitir**, **Sempre permitir nesta sessão** e **Negar**. Reutiliza `AgentWriteApprovalCoordinator`, a bridge do runtime e `AgentApprovalWindow`.
- **Negar** recebe o foco inicial e é a ação segura; Escape, fechar, timeout, cancelamento do turno e broker indisponível negam. Texto do modelo nunca aprova.
- "Sempre nesta sessão" é mantido **só em memória** da sessão/aba, com escopo exato; some ao fechar a sessão/aba, trocar de modo/provider, fazer logout ou reiniciar. Não é persistido nem copiado para outra aba.
- Destrutivas nunca são aprovadas automaticamente por padrão e só se tornam elegíveis a "sempre" após confirmação específica (identificação do alvo). Classificação conservadora: na dúvida, destrutiva.
- Modos `bypassPermissions`, `acceptEdits`, `auto` e `dontAsk` são proibidos; usa-se `--permission-mode default`.
- A tool de permissão não pode ser chamada pelo modelo como tool comum (validar no spike); argumentos são dados não confiáveis e aparecem saneados.
- Exceções que a CLI não submete ao diálogo (regras `permissions.allow` do usuário, configurações gerenciadas, leituras permitidas sem prompt) são neutralizadas por `--setting-sources`/cwd ou exibidas como limitação; nunca omitidas.
- Cada decisão gera evento de auditoria sem comando completo, conteúdo de arquivo ou saída.
