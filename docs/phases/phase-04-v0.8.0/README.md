# Fase 4 — v0.8.0: abertura e salvamento de arquivos de texto

**Situação:** Implementada no código e validada por testes automatizados. A homologação nativa de Windows/Linux permanece pendente.

## Objetivo

Abrir arquivos no editor como texto simples, salvar o conteúdo atual e implementar **Salvar como**. O usuário também pode abrir uma pasta como workspace, navegar pelos arquivos no painel lateral e criar, renomear ou enviar itens à lixeira.

## Escopo incluído (IDs do catálogo)

- EDT-04 (recorte de arquivos) — abrir arquivo como texto simples, salvar a aba e salvar como.
- UX-01 — estados de arquivo modificado, recuperação de rascunho e mensagem de falha de escrita visível.
- UX-01 (recorte de navegação) — painel lateral com as abas **Conexões** e **Arquivos**, raiz única de workspace e árvore de arquivos com carregamento sob demanda.
- CON-10 — abrir/trocar/fechar pasta, criar arquivo ou pasta, renomear e excluir pela lixeira do sistema.

## Fora de escopo

Interpretação semântica do arquivo aberto, importação de dados, múltiplas raízes, Git, sincronização com servidor, monitoramento contínuo e execução automática do conteúdo.

## Antecipações técnicas presentes no código

A barra superior já expõe **Abrir**, **Salvar** e **Salvar como…** (`MainWindow.axaml`), e existe `LocalScriptFileService` em Infrastructure. O recorte desta fase é formalizar o contrato de texto simples, os estados de modificação, a navegação da pasta e o aceite correspondente — não reimplementar o que existe.

## Critério de aceite

Abrir arquivo de texto sem alterar seu conteúdo ou codificação; salvar preservando bytes, BOM e quebras de linha; **Salvar como** criando novo destino sem perder a aba; abrir pasta exibindo o painel Arquivos; criar/renomear/excluir com proteção de raiz e lixeira; falha de escrita visível e não silenciosa; nenhuma execução implícita do conteúdo aberto.

## Dependências

Fase 3 aceita.

## Documentos relacionados

- [Guia de uso](../../14-guia-de-uso.md) · [Editor/BSON](../../06-editor-bson-e-uuid.md) · [Design system](../../17-design-system-ui-ux.md)

## Pendências de homologação real

Diálogos nativos de arquivo e pasta, lixeira do sistema e leitor de tela em Windows e Linux não são homologados por teste Headless.
