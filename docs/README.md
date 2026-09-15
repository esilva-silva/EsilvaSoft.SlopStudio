# Documentação do EsilvaSoft.SlopStudio

Referência: **13/09/2026**. Produto desktop .NET 10/Avalonia, Windows/Linux, LiteDB e MIT. **Atual:** checkout posterior à tag alpha local v0.1.1-alpha; release remota não verificada. **Próxima:** v0.5.0, em desenvolvimento. **Futuro:** v0.6.0–v1.0.0 e backlog sem versão comprometida.

## Comece por aqui

Abra [o índice HTML](index.html) para navegar pelo índice lateral e ler documentos, tabelas e exemplos no painel principal. O leitor acompanha o tema do sistema e funciona sem internet. Links entre Markdown permanecem no próprio painel.

Ao editar ou adicionar arquivos `.md`, execute `node scripts/build-docs-index.cjs` na raiz do repositório para atualizar a cópia de leitura local. Quando servido por HTTP, o leitor busca o conteúdo atual dos arquivos; a lista de documentos continua sendo gerada por esse comando. O renderizador Marked e sua licença MIT estão em `docs/assets/`.

1. [Roadmap por versão](09-plano-de-implementacao.md): seis fases, objetivos, inclusões/exclusões, aceite, dependências e status.
2. [Inventário de implementação](24-inventario-roadmap.md): recortes verificados em código e testes; lacunas reais.
3. [Catálogo funcional](03-catalogo-funcional.md): IDs preservados, versão e status de cada requisito.
4. [Guia de uso](14-guia-de-uso.md): operações disponíveis, sem apresentar planos como opções existentes.
5. [Matriz de validação](15-matriz-de-validacao.md) e [checklist de homologação](16-checklist-homologacao.md): separar teste automatizado de servidor, SO, hardware e UI nativos.

✅ Implementado indica o recorte descrito no código, não uma release homologada. 🚧 Em desenvolvimento indica implementação parcial; 📋 Planejado, ausência de caminho integrado; 🧪 Experimental, limites de qualidade/ambiente. Itens amplos do catálogo só fecham quando todo seu aceite aplicável tem evidência. Formatação geral de query/script e CSV são lacunas explícitas da v0.5.0.

## Documentos por assunto

| Assunto | Documentação |
| --- | --- |
| Produto e UX | [01 — Visão/escopo](01-visao-e-escopo.md), [02 — Experiência](02-mercado-e-experiencia.md), [17 — Design system](17-design-system-ui-ux.md), [18 — Identidade visual](18-identidade-visual.md) |
| Contratos e arquitetura | [04 — Compatibilidade](04-compatibilidade-e-capacidades.md), [05 — Arquitetura](05-arquitetura.md), [10 — ADRs](10-decisoes-arquiteturais.md) |
| Dados e operação | [06 — Editor/BSON/UUID](06-editor-bson-e-uuid.md), [07 — Segurança/administração](07-dados-seguranca-e-administracao.md), [13 — Transferência lógica](13-exportacao-logica.md) |
| Editor e exploração | [19 — Explorer](19-database-explorer.md), [20 — Console](20-console.md), [22 — Highlighting](22-syntax-highlighting.md) |
| Inteligência opcional | [21 — Autocomplete local](21-autocomplete-local.md), [23 — ONNX/chat](23-onnx-slopcoder.md), [26 — IA local multimodelo](26-ia-local-multimodelo.md), [Autocomplete MongoDB — padrão e plano](auto-complite/README.md) |
| Qualidade e histórico | [08 — Testes](08-testes-e-qualidade.md), [11 — Fontes/evidências](11-fontes-e-evidencias.md), [12 — Acompanhamento](12-acompanhamento-da-implementacao.md) |

## Rastreabilidade e manutenção

Usar os IDs CON/DAT/EDT/AGG/IDX/TRF/ADM/ADV/UX e a versão alvo em issues, PRs e critérios de teste. Os 68 IDs originais permanecem; EDT-08 torna explícito o requisito de formatação do MVP. Para mudança de comportamento, atualizar catálogo, inventário, roadmap, guia e decisões afetadas.

O roadmap substitui F0–F7: referências a essas fases em registros datados são históricas e não correspondem às seis fases novas. Recursos avançados já presentes são antecipações mantidas, não trabalho a repetir. Datas e contagens antigas são evidência da revisão indicada, não validação automática do checkout atual.

Esta revisão é documental: inspeção estática da solução e verificação dos documentos; sem alteração de UI/runtime, nova execução NUnit ou homologação externa. Referências comparativas a ferramentas proprietárias foram retiradas; documentação técnica de protocolos, dependências e capacidades mantém sua finalidade de implementação. Avisos e licenças de terceiros permanecem preservados.


Revisão posterior de código e testes: [25 — Auditoria do MVP e performance](25-auditoria-mvp-performance.md), com lista inicial, entregas, medições e gates restantes.


## Autocomplete: plano revisado

[Arquitetura, quatro modalidades, schema learning persistente e agentes](auto-complite/README.md), revisão de 15/09/2026.
