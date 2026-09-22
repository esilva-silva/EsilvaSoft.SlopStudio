# Roadmap de evolução do Slop Studio

Referência: **21/09/2026**. Este plano substitui o cronograma antigo F0–F7 e a numeração anterior de seis fases. As fases são compromissos de consolidação, não a ordem em que todo código foi escrito. Recursos antecipados continuam disponíveis com seus limites; sua existência não encerra uma fase.

**Versão atual identificável:** última tag alpha local `v0.1.1-alpha` (também existe `v0.1.0.alpha`); o checkout contém alterações posteriores. Não foi verificada publicação remota. Os projetos não fixam versão de produto; o workflow de release recebe a versão da tag. A v0.5.0 teve o escopo funcional fechado e está [arquivada](done/release_v0.5.0/README.md), sem homologação. **Versão em execução: v0.6.0.** Esta revisão não altera tags ou binários.

## Status e evidências

- ✅ **Implementado:** caminho concreto no código para o recorte descrito; não significa homologação em todas as plataformas/topologias.
- 🚧 **Em desenvolvimento:** implementação parcial ou critérios essenciais pendentes, explicitados junto do status.
- 📋 **Planejado:** sem caminho integrado identificado para o recorte.
- 🧪 **Experimental:** caminho disponível, mas qualidade ou ambiente limita seu uso como compromisso estável.

O [catálogo](03-catalogo-funcional.md) mantém IDs e status do requisito completo; o [inventário de código](24-inventario-roadmap.md) discrimina recortes existentes e lacunas. A [matriz](15-matriz-de-validacao.md) registra testes executados e o [checklist](16-checklist-homologacao.md) mantém verificações externas. Nenhuma validação textual equivale à publicação de uma release ou ao encerramento dos requisitos amplos do catálogo.

## Meta transversal — internacionalização da interface — concluída no recorte automatizado

A interface desktop suporta `pt-BR`, `en`, `es` e `zh-CN`. `pt-BR` permanece o idioma inicial para preservar a experiência existente; valores ausentes ou inválidos e chaves sem tradução resolvem para `en`, e uma chave também ausente no inglês aparece como `[[chave]]`. A preferência é persistida de forma aditiva na sessão, sem regravar rascunhos, resultados ou credenciais. Os arquivos `docs/**/*.md` continuam deliberadamente em português.

O incremento cobre catálogo, barra principal, Explorer, editor/resultados, ferramentas, conexões, ambientes, histórico, autocomplete/IA, acessibilidade, mensagens de operação e inicialização com restauração do idioma antes das operações do workspace. A matriz visual gera 64 PNGs reais nos quatro idiomas e nos dois temas. A meta de implementação e tradução está concluída: os testes unitários e a evidência visual estão verdes. Revisão linguística de domínio e validação integrada oficial com MongoDB permanecem como homologação externa das fases, não como bloqueio do catálogo de idiomas.

| Fase | Versão | Objetivo | Situação |
| --- | --- | --- | --- |
| 1 | v0.5.0 | MVP: conectar → navegar → consultar → visualizar → editar → exportar | [Escopo funcional implementado](phases/phase-01-v0.5.0/README.md); aguardam-se validações de aceitação para Windows/Linux e demais [pendências registradas](done/release_v0.5.0/pendencias-de-homologacao.md) |
| 2 | v0.6.0 | Organização dos projetos e autocomplete básico | [**Em execução**](phases/phase-02-v0.6.0/README.md). Núcleos separados e autocomplete determinístico implementados no recorte automatizado; aceite e homologação ainda pendentes |
| 3 | v0.7.0 | Autocomplete com IA | [Planejada](phases/phase-03-v0.7.0/README.md). Sugestões assistidas por modelo local, sempre revisáveis e sem aplicação automática |
| 4 | v0.8.0 | Abertura e salvamento de arquivos de texto | [Planejada](phases/phase-04-v0.8.0/README.md). Há antecipação de Abrir/Salvar/Salvar como; faltam aceite integrado e homologação de diálogos nativos |
| 5 | v0.9.0 | IA local e produtividade contextual | [Experimental](phases/phase-05-v0.9.0/README.md). Modelos ONNX, ghost text, propostas de chat revisáveis, catálogo multimodelo e seleção de CPU/GPU/NPU |
| 6 | v0.10.0 | Administração e manutenção | [Em desenvolvimento](phases/phase-06-v0.10.0/README.md). Coleções, views, validação, índices, estatísticas, usuários, papéis e exportação/importação lógica |
| 7 | v0.11.0 | Chat simples com IA baseado em workflow | [Planejada](phases/phase-07-v0.11.0/README.md). Fluxo predefinido, escopo limitado e ações controladas |
| 8 | v1.0.0 | Estabilidade, revisão completa, instalação e atualizações | [Planejada](phases/phase-08-v1.0.0/README.md). Release estável somente após revisão de código, arquitetura, segurança, testes, instalação e atualização |

O detalhamento de cada fase — escopo, fora de escopo, antecipações, aceite e dependências — fica em [`phases/`](phases/README.md). Este documento permanece como índice e regra geral.

## Implementação antecipada, fase ativa, backlog e release arquivada

Quatro estados distintos, que não devem ser confundidos:

- **Fase ativa** — a única fase em execução. Hoje é a [Fase 2 / v0.6.0](phases/phase-02-v0.6.0/README.md). É o único escopo que justifica novas entradas na interface.
- **Implementação antecipada** — código integrado que pertence a uma fase futura. Existe, é preservado e continua testado, mas **não** encerra a fase à qual pertence, **não** conta como escopo concluído da fase atual e **não** aparece na interface. Exemplos atuais: administração (v0.10.0), agregação e Script Engine (sem fase), IA local (v0.9.0).
- **Backlog** — requisito sem fase definida, adiado ou retirado do escopo. Ver [`backlog/`](backlog/README.md). Entrar no backlog não apaga código: a implementação é preservada e isolada, apenas sem ponto de entrada visual.
- **Release arquivada** — versão cujo escopo funcional foi fechado e movido para [`done/`](done/README.md). Arquivar **não** significa homologar; pendências de homologação continuam registradas na própria pasta da release.

Recursos fora da fase podem continuar presentes no código, desativados ou experimentais. Sua existência não é compromisso de suporte.

## Requisitos sem versão comprometida

O backlog de requisitos sem fase atribuída está em [`backlog/bkl-05-requisitos-sem-fase.md`](backlog/bkl-05-requisitos-sem-fase.md), preservando os IDs do catálogo. Nenhum item ali bloqueia automaticamente a v1.0.0.

## Ordem de trabalho e definição de pronto

1. Concluir a Fase 2 / v0.6.0: separação dos núcleos e autocomplete determinístico simples.
2. Homologar o ciclo básico da v0.5.0 nos SOs alvo; resolver regressões sem alterar asserções para ocultá-las.
3. Avançar pelas fases 3 a 7 na ordem do roadmap, reutilizando implementações antecipadas e preservando proteções.
4. Fechar instalação, atualização, acessibilidade e estabilidade da v1.0.0.

Cada issue deve indicar ID, versão, recorte, fora de escopo, dependências, cenário Given/When/Then, testes, documentação e status. Encerramento requer evidência proporcional; Headless não substitui MongoDB, modelo, cofre, leitor de tela ou diálogo nativo. Não há estimativa de calendário aprovada.

Referências antigas F0–F7 em registros datados são históricas: F0 era fundação, F1 alpha, F2 MVP, F3 análise/recuperação, F4 antiga 1.0 e F5–F7 extensões. Não correspondem numericamente às oito fases atuais, nem à numeração de seis fases usada até 17/09/2026; usar versão e ID em novos trabalhos. Decisão em [ADR-035](10-decisoes-arquiteturais.md#adr-035--roadmap-por-versão-e-status-baseado-em-evidência-13092026).


## Revisão do plano de autocomplete — 15/09/2026

As fases 1 a 5 citadas nesta seção são sub-fases do subsistema de autocomplete, com numeração própria, e não as oito fases do produto. Plano revisado: dados existentes com aceite parcial; a sub-fase 1 amplia para aprendizado persistente de find; a sub-fase 2 fornece contexto/lista/presenter; 5.1 tradicional pode seguir a 2 sem IA; 3/4 preparam IA; 5.2 IA e 5.3 híbrido sequencial. Dez perfis e tarefas com dependências estão no plano executável. Não há implementação nova nesta revisão. [Plano revisado](auto-complite/README.md), [tarefas por agente](auto-complite/execution-plan.md) e [schema learning](auto-complite/schema-learning.md).
