# Backlog

Requisitos **sem fase definida**, adiados, ou retirados do escopo atual por decisão arquitetural. Trabalho planejado ou em execução fica em [`../phases`](../phases/README.md); requisitos concluídos ficam em [`../done`](../done/README.md).

## Critério de entrada

Um requisito entra aqui quando: (a) não tem fase atribuída no [roadmap](../09-plano-de-implementacao.md); (b) foi adiado por decisão registrada; ou (c) é uma implementação antecipada cuja fase foi removida do planejamento atual.

Entrar no backlog **não apaga código**. Implementações existentes são preservadas e isoladas em seus projetos; apenas os pontos de entrada na interface são removidos.

## Critério de saída

Um requisito sai do backlog quando recebe fase e versão no roadmap oficial, com escopo, aceite e dependências declarados. A reativação dos pontos de entrada na interface faz parte do aceite da fase que o receber.

## Documentos

| Documento | Assunto | Código preservado? |
| --- | --- | --- |
| [bkl-01 — Cofre criptográfico de ambientes](bkl-01-key-vault-criptografico.md) | Cofre criptográfico real para segredos de ambiente | Sim — armazenamento local existente permanece em uso |
| [bkl-02 — Ferramentas fora de fase](bkl-02-ferramentas-fora-de-fase.md) | Recortes da janela de Ferramentas não priorizados | Sim — janela e ViewModels intactos |
| [bkl-03 — Script Engine entre conexões](bkl-03-script-engine-entre-conexoes.md) | Automação JavaScript entre múltiplas conexões | Sim — Console Jint e modo mongosh intactos |
| [bkl-04 — Modo Aggregation](bkl-04-modo-aggregation.md) | Modo Agregação da tela inicial | Sim — parser, validador e contratos intactos |
| [bkl-05 — Requisitos sem fase](bkl-05-requisitos-sem-fase.md) | IDs do catálogo sem versão comprometida | Parcial — ver documento |
| [27 — Consultas avançadas](27-consultas-avancadas.md) | Auditoria histórica da antiga meta de agregação | Documento preservado como evidência histórica |
