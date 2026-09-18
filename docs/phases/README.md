# Fases do EsilvaSoft.SlopStudio

Referência: **18/09/2026**. Esta pasta contém o trabalho **planejado ou em execução** por fase. Requisitos concluídos e aceitos são arquivados em [`../done`](../done/README.md); requisitos sem fase definida, adiados ou retirados do escopo ficam em [`../backlog`](../backlog/README.md).

O [roadmap](../09-plano-de-implementacao.md) permanece na raiz da documentação como índice e regra geral; cada fase detalha o próprio escopo aqui.

## Roadmap oficial

| Fase | Versão | Objetivo | Situação |
| --- | --- | --- | --- |
| 1 | v0.5.0 | MVP: conectar → navegar → consultar → visualizar → editar → exportar | [Arquivada por escopo funcional](phase-01-v0.5.0/README.md) — homologação pendente |
| 2 | v0.6.0 | Organização dos projetos e autocomplete básico | [**Em execução**](phase-02-v0.6.0/README.md) |
| 3 | v0.7.0 | Autocomplete com IA | [Planejada](phase-03-v0.7.0/README.md) |
| 4 | v0.8.0 | Abertura e salvamento de arquivos de texto | [Planejada](phase-04-v0.8.0/README.md) |
| 5 | v0.9.0 | IA local e produtividade contextual | [Experimental](phase-05-v0.9.0/README.md) |
| 6 | v0.10.0 | Administração e manutenção | [Em desenvolvimento](phase-06-v0.10.0/README.md) |
| 7 | v0.11.0 | Chat simples com IA baseado em workflow | [Planejada](phase-07-v0.11.0/README.md) |
| 8 | v1.0.0 | Estabilidade, revisão completa, instalação e atualizações | [Planejada](phase-08-v1.0.0/README.md) |

## Situações

- **Em execução:** fase ativa. É o único escopo que justifica novas entradas na interface.
- **Em desenvolvimento:** existe código integrado antecipando a fase, mas a fase não está ativa. O código é preservado; a interface não o oferece.
- **Planejada:** sem caminho integrado comprometido para o recorte.
- **Experimental:** caminho disponível, com qualidade ou ambiente limitando o uso como compromisso estável.
- **Arquivada:** escopo funcional fechado e movido para `../done/release_vX.Y.Z`; pendências de homologação continuam registradas.

## Implementação antecipada não encerra fase

Código existente para uma fase futura é **antecipação técnica**. Ele não transfere a fase para "concluída", não gera entrada na interface e não dispensa o aceite próprio da fase. Cada documento de fase lista suas antecipações em seção separada do escopo.

## Preservação de IDs

Os IDs do [catálogo funcional](../03-catalogo-funcional.md) (CON/DAT/EDT/AGG/IDX/TRF/ADM/ADV/UX) são preservados em qualquer movimentação. O [inventário](../24-inventario-roadmap.md) registra a evidência em código e a localização atual de cada requisito.
