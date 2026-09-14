# Roadmap de evolução do Slop Studio

Referência: **13/09/2026**, código local `e806ae4`. Este plano substitui o cronograma antigo F0–F7. As fases são compromissos de consolidação, não a ordem em que todo código foi escrito. Recursos antecipados continuam disponíveis com seus limites; sua existência não encerra uma fase.

**Versão atual identificável:** última tag alpha local `v0.1.1-alpha` (também existe `v0.1.0.alpha`); o checkout contém alterações posteriores. Não foi verificada publicação remota. Os projetos não fixam versão de produto; o workflow de release recebe a versão da tag. **Próxima versão alvo: v0.5.0**, ainda sem aceite. Esta revisão não altera tags ou binários.

## Status e evidências

- ✅ **Implementado:** caminho concreto no código para o recorte descrito; não significa homologação em todas as plataformas/topologias.
- 🚧 **Em desenvolvimento:** implementação parcial ou critérios essenciais pendentes, explicitados junto do status.
- 📋 **Planejado:** sem caminho integrado identificado para o recorte.
- 🧪 **Experimental:** caminho disponível, mas qualidade ou ambiente limita seu uso como compromisso estável.

O [catálogo](03-catalogo-funcional.md) mantém IDs e status do requisito completo; o [inventário de código](24-inventario-roadmap.md) discrimina recortes existentes e lacunas. A [matriz](15-matriz-de-validacao.md) registra testes executados e o [checklist](16-checklist-homologacao.md) mantém verificações externas. Nenhuma fase abaixo está concluída.

| Fase | Versão alvo | Objetivo | Status atual | Dependência |
| --- | --- | --- | --- | --- |
| 1 | v0.5.0 | MVP: conectar → navegar → consultar → visualizar → editar → exportar | 🚧 Em desenvolvimento | Fundação existente; fechar lacunas do MVP |
| 2 | v0.6.0 | Queries e pipelines complexos com produtividade | 🚧 Em desenvolvimento; ferramentas antecipadas | Aceite v0.5.0 |
| 3 | v0.7.0 | Administração e manutenção cotidiana | 🚧 Em desenvolvimento; operações já expostas | Aceite v0.6.0 e proteção de escrita |
| 4 | v0.8.0 | Automação JavaScript entre conexões | 🚧 Em desenvolvimento; Console e mongosh existentes | Aceite v0.7.0; contratos BSON e execução isolada |
| 5 | v0.9.0 | Inteligência local e produtividade contextual | 🧪 Experimental na IA; base determinística existente | Aceite v0.8.0; contexto, privacidade e cancelamento |
| 6 | v1.0.0 | Estabilidade e distribuição para uso diário | 📋 Planejado como release; infraestrutura parcial | Aceites anteriores e gates de release |

## Fase 1 — v0.5.0: MVP

**Objetivo:** concluir o ciclo diário básico sem exigir IA ou automação entre servidores.

| Funcionalidade incluída | Estado e limite atual | Aceite para a versão |
| --- | --- | --- |
| Conexões e Explorer: Connection → Database → Collection → Documents | ✅ Implementado: perfis, abertura, árvore sob demanda e detalhes básicos | Cadastrar/testar/abrir conexão, listar bancos/coleções; abrir coleção prepara consulta sem executar; execução explícita mostra documentos |
| `find`, `findOne`, filtro, `sort`, `limit`, `skip` | ✅ Implementado no Console/driver; filter é o argumento de find, não um método novo | Filtro/ordenação/paginação verificáveis em fixture real, vazio/erro/limite visíveis e cancelamento sem cruzar abas |
| Insert, Update, Delete e edição JSON/BSON | ✅ Implementado no recorte básico; homologação de edição real pendente | Revisar antes de enviar; preservar BSON/UUID/datas; impedir substituição indevida de documento projetado; conflito e confirmação destrutiva observáveis |
| Autocomplete simples | ✅ Implementado: comandos, operadores, nomes conhecidos, campos e contexto/histórico local | Funcionar sem pesos de IA, não consultar servidor a cada tecla e não misturar conexão/aba |
| Formatação JSON, query e script | ✅ Implementado: comando em Opções do editor, seleção/conteúdo completo, worker, cancelamento e undo; limite de 1 milhão de caracteres | Formatar JSON e `db.Users.find({"Active":true})` em múltiplas linhas sem executar ou mudar semântica; preservar strings, comentários, regex e construtores BSON; permitir desfazer |
| Exportação JSON e CSV | ✅ Implementado no escopo da página: JSON/CSV incremental, progresso real, cancelamento e publicação após sucesso | Escolher formato e escopo; JSON preserva tipos; CSV segue contrato abaixo; verificar escape, aninhamento e erro de escrita |
| Base existente: abas, histórico, arquivos, rascunhos, temas, identificadores e auditoria local | ✅ Implementado nos recortes do inventário; acessibilidade e SOs têm gates separados | Recuperar rascunho sem resultados/credenciais no snapshot; opt-out respeitado; falha de persistência visível; teclado/temas sem regressão |

**Contrato CSV implementado (TRF-01):** exportar somente a página carregada, explicitamente indicada. Cabeçalhos serão a união dos campos de primeiro nível em ordem de primeira ocorrência. Objetos, arrays e valores BSON tipados serão serializados em Extended JSON canônico dentro da célula, sem flattening implícito. Campo ausente será célula vazia; null será o literal `null`; strings vazias usarão aspas. Aspas internas serão duplicadas; delimitador, quebras de linha e aspas exigirão célula entre aspas. UTF-8, separador vírgula e cabeçalho presente. CSV será intercâmbio tabular, sem promessa de round-trip BSON ou distinção universal de tipos por leitores externos. A política de fórmulas é explícita no formato e na mensagem de conclusão. A UI oferece Extended JSON e CSV no seletor de arquivo. Strings e cabeçalhos que começam, após espaços, por `=`, `+`, `-` ou `@`, ou por tab/CR/LF, recebem apóstrofo inicial. Isso altera intencionalmente essas células para proteção de fórmulas.

**Fora do aceite obrigatório:** pipeline avançado, administração completa, backup/restauração, importação CSV, Script Engine entre conexões e IA. As implementações antecipadas são extras disponíveis no checkout, sem duplicar trabalho nem impor sua consolidação à v0.5.0.

**Dependências:** driver, proprietário LiteDB em DI, snapshots de contexto, cancelamento por aba e formato exportado documentado. **Saída:** demonstrar conectar → navegar → consultar → visualizar → editar → exportar em Windows/Linux nos ambientes declarados; testes BSON/falha/concorrência e evidência visual dos fluxos alterados; CSV e formatação geral fechados. Os gaps de implementação de CSV e formatação foram fechados; os gates de homologação ainda impedem declarar a versão pronta. Consulte a [auditoria](25-auditoria-mvp-performance.md).

**Documentação:** [inventário](24-inventario-roadmap.md), [guia](14-guia-de-uso.md), [Explorer](19-database-explorer.md), [editor/BSON](06-editor-bson-e-uuid.md), [exportação](13-exportacao-logica.md), [design system](17-design-system-ui-ux.md).

## Fase 2 — v0.6.0: consultas avançadas

**Objetivo:** construir e analisar queries e pipelines complexos sem depender de outra ferramenta para o escopo declarado.

**Incremento de 14/09/2026:** validação offline com localização, catálogo dos 12 stages e explain bruto de aggregation em `queryPlanner`; fixture real combinando os 12 stages aprovada. O aceite completo continua em aberto, sobretudo contexto do autocomplete, histórico de Agregação e diagnósticos de servidor. [Detalhes e evidência](27-consultas-avancadas.md).

**Incluído:** criação/execução textual de aggregation com `$match`, `$group`, `$project`, `$sort`, `$limit`, `$skip`, `$lookup`, `$unwind`, `$set`, `$unset`, `$count` e `$facet`; formatação, validação, erros localizáveis, bracket matching, syntax highlighting, histórico e execução parcial quando aplicável. Autocomplete considera Connection, Database, Collection, schema conhecido, query, Input, campos de Results e vocabulário MongoDB. IA é opcional se estável. Revisar cliques, espaço, navegação, atalhos, menus e integração Explorer/Editor/Results.

**Status:** 🚧 Em desenvolvimento. Driver/Console executam pipelines textuais; explain bruto existe nas ferramentas; autocomplete, seleção/statement, highlighting e delimitadores têm código. Não equivalem a cobertura completa por versão de servidor, explain gráfico ou construtor visual. Formatação geral depende da Fase 1. A migração atual do editor para AvaloniaEdit deve ter evidência própria, sem reutilizar conclusões antigas do TextBox.

**Fora do escopo:** administração ampla, novo runtime de automação, exigência de IA, construtor visual obrigatório e suporte irrestrito a qualquer estágio/versão.

**Aceite:** fixtures reais para os 12 stages listados, inclusive joins/arrays; erros de sintaxe/servidor no destino correto; execução parcial não executa outro trecho ao falhar; limites e estágios de escrita protegidos; sugestões sem contexto obsoleto; jornadas medidas e renderizadas nos dois temas. Explain declara a superfície suportada: `explain()` não integra a API atual do Console.

**Dependências:** v0.5.0, catálogo de linguagem/capacidades e fixtures MongoDB. **Documentação:** [catálogo AGG/EDT](03-catalogo-funcional.md), [editor](06-editor-bson-e-uuid.md), [Console](20-console.md), [highlighting](22-syntax-highlighting.md), [autocomplete](21-autocomplete-local.md), [UX](17-design-system-ui-ux.md).

## Fase 3 — v0.7.0: administração e manutenção MongoDB

**Objetivo:** cobrir tarefas rotineiras de manutenção diretamente na aplicação.

**Incluído:** listar/criar/remover índices, alterar somente opções suportadas, gerar/copiar scripts; quantidade, tamanho, storage, índices, opções, validações e metadata de coleção; `createCollection`, `dropCollection`, `renameCollection`, `createIndex`, `dropIndex`, `stats`/`collStats`/`dbStats`. Separar consulta de ação administrativa/destrutiva. Consolidar ferramentas administrativas e transferência lógica já presentes.

**Status:** 🚧 Em desenvolvimento. Serviços/UI oferecem coleções, índices, estatísticas, validação, usuários/papéis e algumas ações de manutenção. Listar uso de índice não implementa análise de redundância/custo de build. Exportação lógica com manifesto existe; integração mongodump/mongorestore permanece planejada e fora do gate mínimo.

**Fora do escopo:** administração de clusters distribuídos, provisionamento de nuvem, automação de SO, backup operacional completo e sincronização entre servidores.

**Aceite:** em servidor real, criar/inspecionar/renomear/remover coleção e criar/alterar opção suportada/remover índice com releitura; script gerado corresponde ao alvo e não executa ao copiar; estatísticas distinguem estimativa e contagem exata. Confirmar drop collection, drop index, deleteMany e operações de banco; respeitar somente leitura, permissões, proteção de `_id_`, auditoria e resultado incerto após cancelamento. Alteração de chave de índice exige recriação revisada, sem prometer alteração in-place.

**Dependências:** v0.6.0, contratos de escrita e capacidade por servidor/permissão. **Documentação:** [catálogo DAT/IDX/ADM](03-catalogo-funcional.md), [segurança/administração](07-dados-seguranca-e-administracao.md), [Explorer](19-database-explorer.md), [transferência lógica](13-exportacao-logica.md), [homologação](16-checklist-homologacao.md).

## Fase 4 — v0.8.0: MongoDB Script Engine

**Objetivo:** automação JavaScript entre conexões, em modo de trabalho explicitamente distinto da consulta convencional.

**Incluído:** navegação programática Connection → Database → Collection → Documents; variáveis, funções e loops; leitura na origem e inserção/transformação no destino; múltiplas conexões num job. Migração, cópia, comparação, sincronização e testes por script são usos pretendidos; assistentes completos e retomada automática não são inferidos da execução de loops.

**Status:** 🚧 Em desenvolvimento. O Console Jint/proxies/driver oferece getConnection e ConnectionPool; o modo Script usa processo mongosh externo. Reutilizar esses caminhos e formalizar a experiência de automação sem engine duplicado ou anunciar APIs equivalentes. A API atual usa `getDatabase` (minúsculo), não `GetDatabase`.

```javascript
// API do Console existente; revisar origem e destino antes de executar.
const source = getConnection("Production").getDatabase("CRM").getCollection("Customers");
const target = getConnection("Development").getDatabase("CRM").getCollection("Customers");
const customers = source.find({ Active: true }).limit(100).toArray();
for (const customer of customers) {
    target.insertOne(customer);
}
```

O exemplo conserva `_id`: duplicados podem falhar e escritas anteriores podem já ter ocorrido. Não é um sincronizador com retomada. A DSL de navegação equivalente é `ConnectionPool.Production.CRM.Customers`; `ConnectionPull` e `GetDatabase` são exemplos conceituais reconhecidos pelo highlighting, mas não são APIs executáveis. Não acrescentar aliases sem decisão explícita.

**Fora do escopo:** paridade total com mongosh no Console, acesso CLR/Node irrestrito, transação distribuída, rollback ao cancelar, sincronização com checkpoint e execução agendada com app fechado.

**Aceite:** dois servidores independentes comprovam leitura/transformação/escrita preservando BSON; snapshots de perfis/ambientes antes dos awaits pertinentes; cada destino aplica permissões e confirmação. Testar credenciais inválidas, somente leitura, falha depois de escrita, duplicado, limites de documentos/saída/memória, timeout, loop infinito, cancelamento e ausência de mongosh. Definir contratos seguros de transformação JS sem presumir fidelidade arbitrária. Homologar cada runtime anunciado em Windows/Linux.

**Dependências:** v0.7.0, contratos de execução/segredos e ambientes reais isolados. **Documentação:** [Console](20-console.md), [script/BSON](06-editor-bson-e-uuid.md), [arquitetura](05-arquitetura.md), [ADRs](10-decisoes-arquiteturais.md), [matriz](15-matriz-de-validacao.md).

## Fase 5 — v0.9.0: inteligência e produtividade

**Objetivo:** assistência técnica local integrada à IDE, mantendo autocomplete determinístico como base.

**Incluído:** modelo local para MongoDB, JSON, aggregation, Atlas Search e DSL; ghost text aceito por Tab; chat técnico pt-BR/en com conexão, banco, coleção, query, schema, Input e Results conforme privacidade. Ações: explicar/otimizar query, converter em aggregation, adicionar filtro por data, agrupar por conta e explicar erro.

**Status:** 🧪 Experimental para modelo/chat. Há ONNX, contexto limitado, ghost text, Tab incremental e propostas com diff. Inferência CPU real tem registros; chat FIM introduziu alteração não solicitada, GPU não está homologada e CUDA compilado não significa geração validada. Não marcar assistência pronta por existir painel.

**Fora do escopo:** chatbot genérico, execução automática de sugestões, envio implícito a serviços externos, garantia de otimização sem explain/medição e obrigatoriedade de GPU.

**Aceite:** casos pt-BR/en para cada ação sem alterações extras não solicitadas; sugestões respeitam linguagem/contexto; Tab/Escape/undo e descarte de resposta obsoleta; funcionamento sem modelo; opt-out impede contexto indevido; cancelamento isolado entre chat/autocomplete; latência/memória documentadas por hardware/modelo. Revisar antes de aplicar e confirmar antes de executar escrita.

**Dependências:** v0.8.0, modelo compatível externo, contexto determinístico, política de dados e avaliações reproduzíveis. **Documentação:** [autocomplete](21-autocomplete-local.md), [ONNX/chat](23-onnx-slopcoder.md), [ADRs](10-decisoes-arquiteturais.md), [matriz](15-matriz-de-validacao.md).

## Fase 6 — v1.0.0: consolidação e release

**Objetivo:** uso diário estável sem depender de recursos experimentais.

**Incluído:** estabilidade, performance, UX, bugs, testes, documentação, segurança, recuperação de sessão, atualização, instalação e compatibilidade Windows/Linux. Consolidar jornadas anteriores; experimentos ainda não aprovados permanecem opcionais e identificados.

**Status:** 📋 Planejado como marco de estabilidade; 🚧 infraestrutura de CI/release e recuperação parcial. Workflow/tag não comprova instalação ou atualização real. A última evidência histórica registra falha de teste; revalidar o checkout antes de fechar o gate.

**Fora do escopo:** grande pacote de funções novas, todos os comandos/topologias MongoDB e conclusão compulsória do backlog especializado.

**Aceite:** nenhum defeito crítico de integridade/segredos aberto; suíte/regressões aprovadas; desempenho medido em datasets definidos; recuperação sem sobrescrever sessão ilegível; instalação/atualização em máquinas limpas Windows/Linux e integridade dos artefatos; teclado, leitor de tela e diálogos nativos homologados; licenças/avisos/SBOM/documentação revisados. Publicar só compatibilidade comprovada, release notes e limites conhecidos.

**Dependências:** aceites anteriores, laboratórios, distribuição e credenciais quando necessárias para assinatura. **Documentação:** [qualidade](08-testes-e-qualidade.md), [compatibilidade](04-compatibilidade-e-capacidades.md), [matriz](15-matriz-de-validacao.md), [checklist](16-checklist-homologacao.md), [avisos de terceiros](../THIRD-PARTY-NOTICES.md).

## Backlog futuro, sem versão comprometida

📋 Planejado: autenticação especializada/SSH, bulk multinamespace/transações, backup/restauração por Database Tools, GridFS, séries temporais, change streams, criptografia em uso, gestão Search/Vector Search, sharding/replicação administrativa, integração Atlas, sincronização revisável/checkpoints, agenda, SQL, migrações versionadas e conectores. 🚧 Parcial: diagnóstico de topologia, schema e transferência lógica, conforme inventário. Preservar IDs do catálogo; não bloquear automaticamente a 1.0.

Capacidades de serviços externos são escopo técnico futuro, não recomendação de aquisição nem nova dependência comercial autorizada. Cada incremento exige decisão de escopo, dependências/licenças, ambiente, segurança, critério observável e evidência própria.

## Ordem de trabalho e definição de pronto

1. Fechar formatação e CSV da v0.5.0 com contratos/testes e guia.
2. Homologar ciclo básico nos SOs alvo; resolver regressões sem alterar asserções para ocultá-las.
3. Consolidar v0.6.0–v0.9.0 reutilizando o que existe e preservando proteções.
4. Fechar instalação, atualização, acessibilidade e estabilidade da v1.0.0.

Cada issue deve indicar ID, versão, recorte, fora de escopo, dependências, cenário Given/When/Then, testes, documentação e status. Encerramento requer evidência proporcional; Headless não substitui MongoDB, modelo, cofre, leitor de tela ou diálogo nativo. Não há estimativa de calendário aprovada.

Referências antigas F0–F7 em registros datados são históricas: F0 era fundação, F1 alpha, F2 MVP, F3 análise/recuperação, F4 antiga 1.0 e F5–F7 extensões. Não correspondem numericamente às seis fases atuais; usar versão e ID em novos trabalhos. Decisão em [ADR-035](10-decisoes-arquiteturais.md#adr-035--roadmap-por-versão-e-status-baseado-em-evidência-13092026).
