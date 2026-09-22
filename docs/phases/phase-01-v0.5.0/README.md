# Fase 1 — v0.5.0: MVP

**Situação:** Arquivada por escopo funcional. A validação manual foi transferida para a [Fase 8 / v0.12.0](../phase-08-v0.12.0/README.md).

A meta transversal de internacionalização associada ao `UX-01` foi concluída no recorte automatizado: `pt-BR`, `en`, `es` e `zh-CN`, fallback em inglês, acessibilidade localizada, troca em execução e persistência segura.

O escopo funcional desta fase foi implementado e está arquivado em [`../../done/release_v0.5.0`](../../done/release_v0.5.0/README.md). Esta página permanece como ponto de entrada da fase; a homologação correspondente é acompanhada na Fase 8.

## Objetivo

Concluir o ciclo diário básico — conectar → navegar → consultar → visualizar → editar → exportar — sem exigir IA ou automação entre servidores.

## Escopo incluído (IDs do catálogo)

- CON-01/02/03/06/08/09 — perfis, conexão, URI, abertura simultânea e descoberta.
- DAT-01/03/04/05/06/08/09 — navegação, `find`/`findOne`, filtro, `sort`, `limit`, `skip`, CRUD protegido, contagem e distinct.
- EDT-01/02/03/04/08 — JSON/árvore, BSON/UUID/ObjectId/datas, autocomplete determinístico simples, histórico/arquivos e formatação.
- TRF-01 — exportação JSON e CSV da página carregada.
- ADM-11, UX-01/02 — auditoria local, abas, temas, cancelamento e recuperação.

## Fora de escopo

Pipeline avançado, administração completa, backup/restauração, importação CSV genérica, Script Engine entre conexões e IA.

## Antecipações técnicas presentes no código

As implementações antecipadas presentes no checkout (agregação, administração, Console entre conexões, IA local) **não** pertencem ao aceite desta fase. Elas estão registradas no [inventário](../../24-inventario-roadmap.md) e nas fases correspondentes.

## Critério de aceite

Testes de BSON, falha e concorrência; evidência visual automatizada dos fluxos alterados; contrato CSV e formatação fechados.

## Dependências

Driver MongoDB, proprietário LiteDB em DI, snapshots de contexto, cancelamento por aba e formato exportado documentado.

## Documentos relacionados

- [Release arquivada v0.5.0](../../done/release_v0.5.0/README.md)
- [Auditoria do MVP e performance](../../done/release_v0.5.0/25-auditoria-mvp-performance.md)
- [Guia de uso](../../14-guia-de-uso.md) · [Explorer](../../19-database-explorer.md) · [Editor/BSON](../../06-editor-bson-e-uuid.md) · [Exportação lógica](../../13-exportacao-logica.md)
- [Fase 8 — homologação manual](../phase-08-v0.12.0/README.md)
