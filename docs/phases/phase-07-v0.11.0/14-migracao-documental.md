# Migração documental e cobertura da análise

**Executada como planejamento em 22/09/2026.** Nenhuma versão de aplicativo, pacote ou funcionalidade foi implementada. A revisão partiu da solução e do conjunto documental, não apenas dos nomes sugeridos no pedido.

## Mapa de preservação

| Origem no checkout inicial | Destino | Tratamento |
| --- | --- | --- |
| Fase 7 / v0.11.0 — workflow | [Fase 8 / v0.12.0](../phase-08-v0.12.0/README.md) | Texto integral preservado; somente título/fase/versão renumerados |
| Fase 8 / v0.12.0 — homologação | [Fase 9 / v0.13.0](../phase-09-v0.13.0/README.md) | Escopo/aceite integral preservado; referências/dependências renumeradas; link adicional aos novos gates |
| Fase 9 / v1.0.0 — estabilidade | [Fase 10 / v1.0.0](../phase-10-v1.0.0/README.md) | Conteúdo preservado; número da fase e pré-requisito atualizados |
| Nova fundação de agentes | [Fase 7 / v0.11.0](README.md) | Documentação própria, sem confundir com chat local existente |

O exemplo de nove fases do pedido não contemplava a fase de homologação já presente no repositório. Preservá-la exige dez fases e v0.13.0. A comparação com `git show HEAD:<caminho-original>` confirma todas as linhas não vazias dos três documentos após substituições de fase/versão/referências; o workflow também conserva a ordem e texto completo. Nenhum requisito, ID de catálogo ou evidência histórica foi removido.

A dependência antiga “Fase 6 aceita” permanece literalmente no workflow como parte do escopo preservado. O uso futuro do Agent Runtime acrescenta dependência técnica da Fase 7 para a integração correspondente, sem ampliar o conjunto fechado de ações ou autorizar serviços externos dentro do workflow. A v0.11.0 não redefine silenciosamente as exclusões da v0.12.0.

## Documentos transversais

Roadmap, README raiz, índice de fases, visão, mercado, catálogo, inventário, acompanhamento, guia, matriz e checklist foram conciliados. Arquivos `done/` conservam evidências e agora apontam para a fase correta de homologação. ADR-035 preserva o roteiro anterior como histórico e acrescenta revisão vigente; ADRs 046–051 são propostas, não implementação. O backlog do cofre continua reconhecendo a limitação atual, com ligação ao novo requisito seguro.

O índice HTML usa snapshot gerado por `scripts/build-docs-index.cjs`; regenerá-lo é necessário para não continuar oferecendo caminhos antigos. O arquivo gerado não é fonte independente. Fases internas de `auto-complite/phases` pertencem ao plano daquele subsistema e **não** são renumeradas como fases de release.

## Confronto da documentação com o código

| Área documental | Conclusão e impacto no plano |
| --- | --- |
| Visão/mercado/catálogo (01–04) | Produto desktop MongoDB, IDs preservados; presença de código não encerra fase nem homologa plataforma. Agentes entram como recorte planejado, sem substituir funcionalidades |
| Arquitetura/ADRs (05/10) | Nove projetos reais, núcleos puros e proprietário LiteDB único; portas históricas conceituais não são APIs existentes. Dois novos projetos bastam inicialmente |
| BSON/editor/Explorer/console (06/19/20/22) | Contexto fixo, execução explícita, BSON/UUID e parsing de console precisam ser preservados. Entrada MCP requer parser literal separado de ENV/JS |
| Segurança/admin/transferência (07/13) | Proteções read-only e auditoria existentes são base, não política completa por agente; não publicar todos os serviços por reflexão |
| Testes/guia/acompanhamento (08/12/14–16) | Evidência automatizada separada de MongoDB real, leitor de tela e SO. Novo plano não fecha gates antigos |
| Roadmap/inventário (09/24) | Homologação já ocupava v0.12.0; migração inclui v0.13.0 e estabilidade na fase 10 |
| Fontes (11) | Fontes atuais de protocolos/autenticação/licenças precisam ser datadas e verificadas; novo registro na fase 7 |
| Design/identidade/temas (17/18/ui) | Manter tokens/medidas/foco/localização; chat é nativo, sem novo design de site. PNGs existentes não provam a UI futura |
| ONNX/multimodelo (21/23/26/models) | Runtime compartilhado, prioridades e capabilities de modelo; proposta FIM não equivale a tool calling. Nenhum fallback externo implícito |
| `auto-complite/` e seus agentes/fases | Contexto, schema aprendido, ranking, preempção e opt-outs permanecem locais; não reaproveitar autorização de autocomplete como consentimento de envio externo |
| `done/` | Histórico funcional/evidência conservados; pendências manuais redirecionadas sem inventar nova homologação |
| `backlog/` | Script entre conexões, administração ampliada, agregado e cofre têm recortes distintos; existência do código não autoriza exposição MCP indiscriminada |
| `legado/` | Imagens históricas de tema preservadas; não servem de protótipo ou prova visual do chat novo |

O documento [13](13-analise-do-codigo.md) registra os caminhos de execução e gaps de todos os projetos. Esta revisão arquitetural não pretende uma auditoria formal linha a linha nem certificação de segurança de código que ainda será escrito.

## Rastreabilidade de referências

Buscar globalmente versões v0.11.0/v0.12.0/v0.13.0, `phase-07`, `phase-08`, `phase-09`, `phase-10`, nomes de workflow/homologação e números posteriores; distinguir texto histórico explícito de links vigentes. Validar links locais e snapshot após geração. Referências a relatórios TRX históricos ausentes no checkout não podem ser recriadas como se houvesse evidência nova; sua indisponibilidade deve ser explicitada. O resultado da validação fica no [relatório da meta](17-validacao-da-meta.md).

## Inventário de documentos revisados

A tabela identifica todo o conjunto Markdown anterior à nova fase, com a decisão de migração ou preservação. Leitura arquitetural e cruzamento de contratos não equivalem a auditoria linha a linha de cada fonte.

| Documento | Tratamento |
| --- | --- |
| [Visão e escopo](../../01-visao-e-escopo.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Direção de experiência do produto](../../02-mercado-e-experiencia.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Catálogo funcional](../../03-catalogo-funcional.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Compatibilidade e capacidades](../../04-compatibilidade-e-capacidades.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Arquitetura proposta](../../05-arquitetura.md) | Referência de tema preservada |
| [Editor, autocomplete, BSON e UUID](../../06-editor-bson-e-uuid.md) | Referência de tema preservada |
| [Transferência, segurança e administração](../../07-dados-seguranca-e-administracao.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Testes NUnit e qualidade](../../08-testes-e-qualidade.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Roadmap de evolução do Slop Studio](../../09-plano-de-implementacao.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Registro de decisões arquiteturais](../../10-decisoes-arquiteturais.md) | Referência de tema preservada |
| [Fontes e evidências](../../11-fontes-e-evidencias.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Acompanhamento da implementação](../../12-acompanhamento-da-implementacao.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Exportação lógica atual](../../13-exportacao-logica.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Guia rápido de uso](../../14-guia-de-uso.md) | Referência de tema preservada |
| [Matriz de validação](../../15-matriz-de-validacao.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Checklist de homologação — Fase 9 / v0.13.0](../../16-checklist-homologacao.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Design system e revisão de UI/UX](../../17-design-system-ui-ux.md) | Referência de tema preservada |
| [Identidade visual — Slop Studio](../../18-identidade-visual.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Database Explorer](../../19-database-explorer.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Console JavaScript](../../20-console.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Autocomplete local — análise e contrato de implementação](../../21-autocomplete-local.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Syntax highlighting MongoDB](../../22-syntax-highlighting.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [ONNX: SlopCoder, CPU/GPU e chat do editor](../../23-onnx-slopcoder.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Inventário de implementação e vínculo com o roadmap](../../24-inventario-roadmap.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [IA local multimodelo: catálogo, hardware e serviço central](../../26-ia-local-multimodelo.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Documentação do EsilvaSoft.SlopStudio](../../README.md) | Referência transversal confrontada com novo plano; status implementado/planejado separado |
| [Autocomplete MongoDB — arquitetura e plano revisados](../../auto-complite/README.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Agentes de autocomplete](../../auto-complite/agents/README.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Agent — AI Completion](../../auto-complite/agents/ai-completion-agent.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Agent — AI Preemptive](../../auto-complite/agents/ai-preemptive-agent.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Agent — Autocomplete Architecture](../../auto-complite/agents/architecture-agent.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Agent — MongoDB Context](../../auto-complite/agents/mongodb-context-agent.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Agent — MongoDB Knowledge](../../auto-complite/agents/mongodb-knowledge-agent.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Agent — ONNX Runtime](../../auto-complite/agents/onnx-runtime-agent.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Agent — Performance](../../auto-complite/agents/performance-agent.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Agent — Testing](../../auto-complite/agents/testing-agent.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Agent — Traditional Completion](../../auto-complite/agents/traditional-completion-agent.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Agent — Traditional Preemptive](../../auto-complite/agents/traditional-preemptive-agent.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Autocomplete por IA](../../auto-complite/ai-autocomplete.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Contexto para IA](../../auto-complite/ai-context.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [architecture.md](../../auto-complite/architecture.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [configuration.md](../../auto-complite/configuration.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Context Engine](../../auto-complite/context-engine.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [current-state.md](../../auto-complite/current-state.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Decisões propostas](../../auto-complite/decisions.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Integração com o editor](../../auto-complite/editor-integration.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Plano executável por agentes](../../auto-complite/execution-plan.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Knowledge Catalog](../../auto-complite/knowledge-catalog.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Estratégia ONNX](../../auto-complite/onnx-strategy.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [performance.md](../../auto-complite/performance.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Revisão independente — parser e completion da Fase 2](../../auto-complite/phase-2-review.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Fase 1 — Dados tradicionais e aprendizado dinâmico](../../auto-complite/phases/phase-1-data-traditional.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Fase 2 — Autocomplete tradicional reformulado](../../auto-complite/phases/phase-2-traditional-autocomplete.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Fase 3 — Disponibilização de dados para IA](../../auto-complite/phases/phase-3-data-ai.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Fase 4 — Autocomplete por IA reformulado](../../auto-complite/phases/phase-4-ai-autocomplete.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Fase 5 — Autocomplete preemptivo](../../auto-complite/phases/phase-5-preemptive.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Autocomplete preemptivo (inline)](../../auto-complite/preemptive-autocomplete.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Ranking](../../auto-complite/ranking.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Pesquisa técnica](../../auto-complite/research.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Descoberta dinâmica e aprendizado de schema](../../auto-complite/schema-learning.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Estratégia de testes](../../auto-complite/testing.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Autocomplete tradicional](../../auto-complite/traditional-autocomplete.md) | Plano de subsistema preservado; fases próprias, isolamento local e opt-outs |
| [Consultas avançadas — antecipação técnica (histórico)](../../backlog/27-consultas-avancadas.md) | Backlog preservado; gates de exposição distintos |
| [Backlog](../../backlog/README.md) | Backlog preservado; gates de exposição distintos |
| [Backlog — cofre criptográfico de ambientes](../../backlog/bkl-01-key-vault-criptografico.md) | Backlog preservado; gates de exposição distintos |
| [Backlog — Ferramentas fora de fase](../../backlog/bkl-02-ferramentas-fora-de-fase.md) | Backlog preservado; gates de exposição distintos |
| [Backlog — JavaScript Script Engine entre conexões](../../backlog/bkl-03-script-engine-entre-conexoes.md) | Backlog preservado; gates de exposição distintos |
| [Backlog — modo Aggregation na tela inicial](../../backlog/bkl-04-modo-aggregation.md) | Backlog preservado; gates de exposição distintos |
| [Backlog — requisitos sem fase comprometida](../../backlog/bkl-05-requisitos-sem-fase.md) | Backlog preservado; gates de exposição distintos |
| [Releases arquivadas](../../done/README.md) | Evidência histórica preservada; homologação redirecionada quando referenciada |
| [Auditoria do MVP e polimento de performance](../../done/release_v0.5.0/25-auditoria-mvp-performance.md) | Evidência histórica preservada; homologação redirecionada quando referenciada |
| [Release v0.5.0 — MVP](../../done/release_v0.5.0/README.md) | Evidência histórica preservada; homologação redirecionada quando referenciada |
| [Registro de transferência de homologação — v0.5.0](../../done/release_v0.5.0/pendencias-de-homologacao.md) | Evidência histórica preservada; homologação redirecionada quando referenciada |
| [Release v0.6.0 — organização dos projetos e autocomplete básico](../../done/release_v0.6.0/README.md) | Evidência histórica preservada; homologação redirecionada quando referenciada |
| [Registro de transferência de homologação — v0.6.0](../../done/release_v0.6.0/pendencias-de-homologacao.md) | Evidência histórica preservada; homologação redirecionada quando referenciada |
| [Release v0.7.0 — autocomplete com IA explícita](../../done/release_v0.7.0/README.md) | Evidência histórica preservada; homologação redirecionada quando referenciada |
| [Registro de transferência de homologação — v0.7.0](../../done/release_v0.7.0/pendencias-de-homologacao.md) | Evidência histórica preservada; homologação redirecionada quando referenciada |
| [Release v0.8.0 — arquivos de texto e workspace local](../../done/release_v0.8.0/README.md) | Evidência histórica preservada; homologação redirecionada quando referenciada |
| [Registro de transferência de homologação — v0.8.0](../../done/release_v0.8.0/pendencias-de-homologacao.md) | Evidência histórica preservada; homologação redirecionada quando referenciada |
| [Fases do EsilvaSoft.SlopStudio](../README.md) | Sequência de release conciliada conforme mapa |
| [Fase 1 — v0.5.0: MVP](../phase-01-v0.5.0/README.md) | Sequência de release conciliada conforme mapa |
| [Fase 2 — v0.6.0: organização dos projetos e autocomplete básico](../phase-02-v0.6.0/README.md) | Sequência de release conciliada conforme mapa |
| [Fase 3 — v0.7.0: autocomplete com IA](../phase-03-v0.7.0/README.md) | Sequência de release conciliada conforme mapa |
| [Fase 4 — v0.8.0: abertura e salvamento de arquivos de texto](../phase-04-v0.8.0/README.md) | Sequência de release conciliada conforme mapa |
| [Fase 5 — v0.9.0: IA local e produtividade contextual](../phase-05-v0.9.0/README.md) | Sequência de release conciliada conforme mapa |
| [Fase 6 — v0.10.0: administração e manutenção](../phase-06-v0.10.0/README.md) | Sequência de release conciliada conforme mapa |
| [Fase 8 — v0.12.0: chat simples com IA baseado em workflow](../phase-08-v0.12.0/README.md) | Sequência de release conciliada conforme mapa |
| [Fase 9 — v0.13.0: homologação manual e validação em ambientes reais](../phase-09-v0.13.0/README.md) | Sequência de release conciliada conforme mapa |
| [Fase 10 — v1.0.0: estabilidade, revisão completa, instalação e atualizações](../phase-10-v1.0.0/README.md) | Sequência de release conciliada conforme mapa |
| [Slop Studio --- Dark Theme Color Palette](../../ui/slop-studio-dark-theme.md) | Referência de tema preservada |
| [Slop Studio — Light Theme Color Palette](../../ui/slop-studio-light-theme.md) | Referência de tema preservada |
