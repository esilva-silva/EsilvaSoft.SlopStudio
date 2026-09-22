# Fase 4 — v0.8.0: abertura e salvamento de arquivos de texto

**Situação:** Planejada. Há antecipação técnica de abrir, salvar e salvar como, mas não há aceite integrado desta fase.

## Objetivo

Abrir arquivos no editor como texto simples, salvar o conteúdo atual e implementar **Salvar como**.

## Escopo incluído (IDs do catálogo)

- EDT-04 (recorte de arquivos) — abrir arquivo como texto simples, salvar a aba e salvar como.
- UX-01 — estados de arquivo modificado, recuperação de rascunho e mensagem de falha de escrita visível.

## Fora de escopo

Interpretação semântica do arquivo aberto, importação de dados, projeto/workspace multiarquivo e sincronização com o servidor.

## Antecipações técnicas presentes no código

A barra superior já expõe **Abrir**, **Salvar** e **Salvar como…** (`MainWindow.axaml`), e existe `LocalScriptFileService` em Infrastructure. O recorte desta fase é formalizar o contrato de texto simples, os estados de modificação e o aceite correspondente — não reimplementar o que existe.

## Critério de aceite

Abrir arquivo de texto sem alterar seu conteúdo ou codificação; salvar preservando quebras de linha originais; **Salvar como** criando novo destino sem perder a aba; falha de escrita visível e não silenciosa; nenhuma execução implícita do conteúdo aberto.

## Dependências

Fase 3 aceita.

## Documentos relacionados

- [Guia de uso](../../14-guia-de-uso.md) · [Editor/BSON](../../06-editor-bson-e-uuid.md) · [Design system](../../17-design-system-ui-ux.md)

## Pendências de homologação real

Diálogos nativos de arquivo em Windows e Linux não são homologados por teste Headless.
