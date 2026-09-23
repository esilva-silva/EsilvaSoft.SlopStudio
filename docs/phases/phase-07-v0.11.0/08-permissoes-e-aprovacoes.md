# Permissões e aprovações

**Proposta — ADR-048/050.** O `IAgentToolRegistry` é a única porta de execução. Descrições de tools, prompt do sistema, MCP annotations, aprovação do cliente externo e permissões do provider não concedem acesso ao MongoDB.

## Decisão efetiva

Permitir somente a interseção: política global ∩ perfil/conexão ∩ principal autenticado ∩ sessão/turno ∩ namespace ∩ operação ∩ escopo de saída de dados. Qualquer negação vence. Conexão read-only nega toda escrita, inclusive índice e administração; a permissão MongoDB do usuário continua sendo a última barreira, não um substituto das anteriores.

`AgentPrincipal` é criado por runtime/broker autenticado, nunca desserializado de argumentos do modelo. Contém origem interna/externa, provider opcional, client/session IDs e revisão de concessões. Contas/modelos são atributos de diagnóstico, não identidade de autorização. Políticas desconhecidas, falha de leitura ou sessão ilegível resultam em negação visível.

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

`Rejeitar` e `Aprovar uma vez` são as ações iniciais. Não há `Aprovar sempre` para escrita, exclusão, índices ou administração. Leitura pode usar concessão de sessão configurada separadamente, limitada a namespace/escopo/deadline. Escape/fechar/reiniciar/cancelar/timeout negam. Um segundo evento com o mesmo approval ID é ignorado; duas requisições concorrentes não consomem o mesmo ticket. Alterar argumento, provider, conexão, usuário MongoDB ou revisão exige nova proposta.

Antes de executar, persistir intenção de escrita com identificador de operação. Se auditoria não grava, não executar. Se o banco já confirmou escrita e gravar desfecho falha, exibir a falha e manter intenção pendente para reconciliação, sem repetir a escrita. Não esconder o sucesso do banco nem afirmar rollback. Reconciliação usa identificação específica e leitura autorizada, não replay automático.

## Riscos e controles adicionais

READ_ONLY limita efeitos de escrita, mas pode expor dados e causar carga: deadline/bytes/rate limit e escopo de privacidade permanecem. WRITE sempre pede aprovação; DESTRUCTIVE exige confirmação explícita e nome do destino. ADMINISTRATIVE fica bloqueado até catálogo específico e homologação RBAC. `$out`, `$merge`, execução JS, `mapReduce`, `$where`, `$function`, comandos arbitrários e resoluções `ENV` não entram nas tools de leitura. Lookup/union e pipelines aninhados validam todos os namespaces; se o parser não prova a autorização, negar.

As mesmas regras valem para tool interna, MCP, adapter OpenAI e adapter Claude. Tools nativas de arquivo/shell dos providers ficam desativadas no primeiro escopo; ligar um MCP protegido e deixar um terminal com acesso aos segredos seria uma rota de contorno inaceitável.
