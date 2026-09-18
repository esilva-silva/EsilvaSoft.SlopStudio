# Releases arquivadas

Esta pasta contém os requisitos **concluídos** de cada versão, com a evidência disponível no momento do arquivamento. Trabalho planejado ou em execução fica em [`../phases`](../phases/README.md); requisitos adiados ou sem fase ficam em [`../backlog`](../backlog/README.md).

| Release | Escopo | Estado do arquivamento |
| --- | --- | --- |
| [`release_v0.5.0`](release_v0.5.0/README.md) | MVP: conectar → navegar → consultar → visualizar → editar → exportar | Escopo funcional fechado. **Homologação Windows/Linux pendente** — ver [pendências](release_v0.5.0/pendencias-de-homologacao.md). |

## Regra de arquivamento

Arquivar uma versão registra que **o escopo funcional foi implementado e revisado**. Não afirma homologação, publicação de release nem aceite externo. Toda versão arquivada mantém um documento `pendencias-de-homologacao.md` enquanto houver gate aberto; esse documento não pode ser removido por reorganização documental.

Teste automatizado e execução Headless **não** encerram pendência de homologação real.
