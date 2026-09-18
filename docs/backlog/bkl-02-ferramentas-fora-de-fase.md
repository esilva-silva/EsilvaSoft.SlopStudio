# Backlog — Ferramentas fora de fase

**Origem:** botão **Ferramentas** da barra superior e entradas equivalentes no menu de contexto do Explorer. **Situação:** ponto de entrada removido; conteúdo dividido entre a [Fase 6](../phases/phase-06-v0.10.0/README.md) e este backlog.

## O que existe hoje e permanece funcionando

`WorkspaceToolsWindow` e seus ViewModels continuam no projeto Desktop, íntegros e cobertos por testes. As seções são: Consulta e schema; Análise (valores distintos, explain, agregações); Operações e dados (transferir, administração, documentos, CRUD em lote, índices); Coleções.

Os serviços que a sustentam (`MongoWorkspaceService`, `ExplorerMetadataService`) continuam em Infrastructure e são usados por outros caminhos.

## Decisão

Removidos da interface:

- o botão **Ferramentas** da barra superior (`MainWindow.axaml`);
- os itens de menu do Explorer que abriam essa janela: "Criar coleção…", "Documentos: inserir / atualizar / excluir…", "Criar / remover índices…" e "Administração do banco / coleção…".

Preservados no menu do Explorer: conectar/desconectar, atualizar, abrir documentos/consulta, ver índices, detalhes e estatísticas, geradores de script e remoção de índice — nenhum deles abre a janela de Ferramentas.

O código não foi removido. A janela continua construível e testável; apenas não tem entrada visual.

## Classificação do conteúdo

| Seção | Classificação |
| --- | --- |
| Coleções, Índices, Administração, Transferir, Documentos, CRUD em lote | [Fase 6 — v0.10.0](../phases/phase-06-v0.10.0/README.md). Reativação faz parte do aceite daquela fase. |
| Agregações e Explain | Backlog — ver [bkl-04](bkl-04-modo-aggregation.md). |
| Painel de script da janela de Ferramentas | Backlog — ver [bkl-03](bkl-03-script-engine-entre-conexoes.md). |
| Consulta e schema | Fase 6, no recorte de metadados de coleção. |

## Escopo futuro

Ao reativar, decidir se o ponto de entrada volta como botão da barra, como menu do Explorer, ou ambos; e revisar os rótulos para que descrevam apenas o que estiver homologado.

## Documentos relacionados

[07 — Segurança e administração](../07-dados-seguranca-e-administracao.md) · [19 — Explorer](../19-database-explorer.md) · [13 — Transferência lógica](../13-exportacao-logica.md)
