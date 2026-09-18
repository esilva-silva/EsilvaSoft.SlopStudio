# Matriz de validação

## Fase 2 — autocomplete tradicional (estado em 16/09/2026)

Build focalizado e suíte UnitTests foram executados em Windows x64: **1126 aprovados, 0 falhas**. Esta evidência cobre testes automatizados; não substitui MongoDB real, leitor de tela, layouts nativos ou métricas p95. `AvaloniaTextSnapshot` e o caminho contextual estão cobertos por testes focados. Permanecem pendentes o corpus MRR/top-K, a matriz de 18 PNGs e os gates de desempenho da UI.

## Autocomplete — Fase 1: catálogo de conhecimento — 14/09/2026

Restore `--locked-mode` da solução aprovado, com lockfiles novos do projeto de benchmarks para Cpu, WinML e Cuda. Build Debug WinML: 0 avisos, 0 erros. Suíte regular: **681 aprovados, 0 falhas, 4 ignorados**, em `tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/fase1-regular.trx`. Os ignorados exigem MongoDB portátil ausente nesta máquina, inclusive o novo `MetadataSourceListsKindsValidatorsIndexesAndSamplesWithoutValues`. Testes Explicit de modelos seguem fora da seleção.

| Área | Evidência local | Validação pendente |
| --- | --- | --- |
| Linguagem MongoDB como dados | Integridade do arquivo; projeção do highlighting como superconjunto do vocabulário anterior; contrato com as globais e proxies reais do bootstrap do Console (Jint) | Revisão por versão de servidor |
| Metadata Cache | Leitura sem bloqueio, single-flight com 50 leituras concorrentes, stale-while-revalidate, backoff, LRU, desconexão descartando resultado tardio, invalidação por chave, amostragem só por ação explícita ou opt-in; 29,9 MB retidos no cenário 1 000 × 1 000 com validator em todas | Carga real com permissões restritas |
| Schema | Validator `$jsonSchema`, índices, resultados (sem valores), amostra com ocorrência, limites de nós e profundidade | Amostragem no servidor real, views e time series |
| Catálogo | Consulta apenas às fontes pedidas, dialeto, prefixo sem `$`, campos mesclados por caminho, completude | — |
| Invalidação | DDL via WorkspaceService e Console, falha sem publicação, write-through do Explorer | MongoDB real |
| Métricas | Tags restritas à lista permitida, sem texto de usuário | — |
| Benchmarks | Projeto BenchmarkDotNet e cenário de memória; números em [performance](auto-complite/performance.md#baseline-medida--fase-1) | Segunda máquina de referência |

UI: nenhum comportamento visual novo; os testes Headless existentes de highlighting e autocomplete continuam aprovados. Não houve homologação nativa nem Linux nesta entrega.

## Incremento de consultas avançadas — 14/09/2026

**Aceite da meta textual:** **650 aprovados, 0 falhas**, `phase2-acceptance.trx`; build sem avisos/erros. `AggregationFieldInferenceTests` acrescenta projeções, remoções, renomeações, joins, `let` e facets. `DerivedFieldSuggestionInsertsAtTheCursorAndPreservesUndoOffline` valida a jornada real do editor sem rede (90,4 ms no ensaio focado, sem generalizar para desempenho nativo). [Auditoria e limites](27-consultas-avancadas.md). Checkpoints abaixo preservam suas contagens históricas.

Revisão posterior: **643 aprovados, 0 falhas**, `phase2-final-audit.trx`. `AggregationHistoryTests`, `MongoCompletionTargetTests`, fixture real de seleção/erro e 18 PNGs adicionais de Histórico. Não confundir esse total com os checkpoints anteriores abaixo; testes Explicit de modelos permanecem fora da execução regular. Detalhes e pendências em [27](27-consultas-avancadas.md).

Suíte completa após o incremento: **623 aprovados, 0 falhas, 0 ignorados** em `tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/phase2-current.trx`. Testes Explicit de modelos IA continuam fora da seleção regular.

Restore locked e build com `-p:UsedAvaloniaProducts=` aprovados. `phase2-analysis.trx`: 29 testes aprovados, incluindo validação offline, proteção de escrita, autocomplete básico contextual, localização no editor e isolamento/cancelamento de explain. MongoDB portátil **8.0.30**: fixture independente dos 12 stages, join, unwind, facet, resultado Bia/5, contagem 2 e plano real aprovados; nenhuma coleção de saída criada. 18 PNGs `aggregation-diagnostic-*` gerados; inspeção de claro 960 e escuro 1366 confirmou seleção do erro e painel de diagnóstico. Validação Headless não comprova leitor de tela ou sessão nativa. [Pendências de aceite](27-consultas-avancadas.md).

Este documento separa o que já possui evidência local do que exige um ambiente MongoDB/mongosh real. Nenhuma linha marcada como pendente deve ser apresentada como homologada.

| Área | Windows | Linux | Evidência local | Validação externa necessária |
| --- | --- | --- | --- | --- |
| Compilação .NET 10 | Aprovada em 10/09/2026, 0 avisos/erros | Aprovada em Ubuntu/WSL2, SDK 10.0.400 | Restore travado e solução compilada nos dois sistemas | Validar distribuição nativa em máquina limpa |
| Testes NUnit | 244/244 aprovados em 10/09/2026 | 244/244 aprovados em 10/09/2026 | Logs TRX e testes de domínio, sessão, concorrência e UI | CI publicada e integração MongoDB/mongosh |
| LiteDB | Código e testes de repositório | Código e testes de repositório | Perfis, histórico, consultas salvas e auditoria usam LiteDB | Reiniciar a aplicação e verificar o arquivo de workspace em cada sistema |
| CRUD BSON | Código implementado | Código implementado | Validações e adaptador do driver oficial | Fixture MongoDB com UUID, datas, arrays, duplicidade e filtros concorrentes |
| Criação e remoção de banco | Código implementado | Código implementado | Criação por coleção inicial e confirmação, remoção protegida por nome | MongoDB real com permissões mínimas, nomes inválidos e atualização do explorer |
| Índices | Código implementado | Código implementado | Chaves BSON, único, esparso, TTL e filtro parcial | Índices reais, erro de permissão, tipos text/hashed/wildcard e impacto de build |
| Views | Código implementado | Código implementado | `create` com `viewOn`, edição protegida via `collMod` e pipeline JSON; opções capped são rejeitadas | MongoDB real, leitura somente, dependências, edição e permissões |
| Validação de coleção | Código implementado | Código implementado | `listCollections` para leitura, `collMod` para alteração e schema inicial inferido de até 200 documentos em memória | Validadores válidos/inválidos, inferência de tipos BSON, `warn`, `off`, permissões e versão do servidor |
| Script JavaScript + JSON | Código implementado | Código implementado | Runner `mongosh --norc`, entrada EJSON e `slop.results`; persistência da entrada somente por opção explícita | `mongosh` instalado, autenticação, cancelamento e script com múltiplas consultas |
| Administração e manutenção | Código implementado | Código implementado | `serverStatus`, `hello`, `currentOp`, usuários, papéis, estatísticas, `validate` completo e `compact` de coleção com confirmação e auditoria | Privilégios mínimos, respostas por versão, impacto de `validate`/`compact`, mensagens de autorização e topologias reais |
| Exportação/importação lógica | Código implementado | Código implementado | Manifesto, Extended JSON e upsert por `_id` | UUID, datas, coleção grande, falha parcial e retomada revisada |
| Interface Avalonia | Headless/Skia aprovado | Headless/Skia aprovado em Ubuntu/WSL2 | Dois temas × três tamanhos × três escalas; modal, editor textual, fonte 20, foco e atalhos | Leitor de tela, seletores nativos e gerenciadores de janelas reais |
| Rascunhos e preferências | Aprovados | Aprovados | Debounce, migração aditiva, reinício sem conexão/resultados, opt-out, descarte e falhas | Avaliar jornadas de uso prolongado com usuários |
| Abas e contexto | Aprovados com executor controlado | Aprovados com executor controlado | Destino capturado, respostas fora de ordem, seleção sem fallback, cancelamento isolado, perfil alterado | Executar os mesmos fluxos contra MongoDB/mongosh reais |

| Consulta editor-first | Documentada | Documentada | Consulta, explain, histórico e autocomplete usam o texto do editor; banco/coleção são contexto da aba; não há formulário paralelo de filtro/ordenação | Homologar ergonomia, acessibilidade e autocomplete com MongoDB/mongosh reais |

## Procedimento mínimo

1. Instalar .NET 10 SDK, MongoDB compatível e `mongosh` no sistema alvo.
2. Criar uma fixture descartável com um usuário de leitura e um usuário de escrita.
3. Executar build e NUnit, guardando o log e a contagem aprovada.
4. Abrir a aplicação, cadastrar duas conexões e exercitar CRUD, índices, view, exportação, administração e script.
5. Repetir no segundo sistema operacional e registrar versão do servidor, versão do shell, topologia e privilégios.
6. Atualizar a coluna de evidência deste documento e o [acompanhamento da implementação](12-acompanhamento-da-implementacao.md) somente após a execução real.

O modo script não deve receber senha resolvida em argumentos. O runner aceita URI direta ou interpolada pelo ambiente do processo filho; integração com cofre nativo permanece pendente.

## Baseline e evidência atual

A baseline anterior à revisão era de 229 testes aprovados no Windows. Em 10/09/2026, a revisão passou com 244 testes no Windows e no Ubuntu/WSL2, incluindo renderização Headless/Skia. Os logs estão relacionados no acompanhamento. Homologação com MongoDB/mongosh, diálogos nativos, leitor de tela e gerenciadores de janelas reais continua pendente.

Após a alteração de visibilidade de índice, build sem avisos nem erros e 204 testes NUnit aprovados foram confirmados no Windows, sem consumir crédito de reset. Após incluir o cadastro rápido de URI, a duplicação de prévia de documento e a `wildcardProjection`, a solução continuou sem avisos nem erros e 218 testes NUnit foram aprovados, também sem consumir crédito de reset. A URI fornecida foi exercitada somente na análise local; a automação nativa desta sessão não expôs a janela Avalonia para validação visual.

## Reprodução da revisão UI/UX

Windows: restore travado, `dotnet build EsilvaSoft.SlopStudio.slnx --no-restore -p:UsedAvaloniaProducts=` e `dotnet test EsilvaSoft.SlopStudio.slnx --no-build --no-restore --logger trx`.

Linux: SDK oficial 10.0.400 instalado temporariamente no Ubuntu; `dotnet test EsilvaSoft.SlopStudio.slnx --artifacts-path <pasta-isolada> -p:UsedAvaloniaProducts= -p:RestoreLockedMode=true --logger trx`. Artefatos locais desta execução: `.cache/linux-validation`, isolados de bin/obj do Windows.

A opção UsedAvaloniaProducts vazia evita somente a tarefa de telemetria de build no ambiente restrito. Não relaxa testes ou analisadores. Os últimos TRX são identificados no [acompanhamento](12-acompanhamento-da-implementacao.md).


## Identidade visual — revisão de 10/09/2026

| Verificação | Windows nesta revisão | Linux nesta revisão |
| --- | --- | --- |
| Restore travado / build | Aprovados, 0 avisos e 0 erros | Não reexecutados: Ubuntu sem SDK .NET 10 compatível |
| NUnit e Headless/Skia | 244/244; contraste, barra nos limites da janela, fonte 20, modal, estado vazio e ícone | Pendente; o 244/244 Linux acima é da baseline anterior |
| Inspeção visual | PNGs reais dos dois temas, 3 tamanhos × 3 escalas; folhas de contato e imagens detalhadas | Pendente |
| Shell / ícone nativo / leitor de tela | Pendente | Pendente |

Assets, reprodução e prévias: [identidade visual](18-identidade-visual.md). Não houve alteração de golden files nem redução dos limites de contraste. O teste detectou 4,497:1 no primeiro azul claro escolhido; a cor foi corrigida para #1B6EDC.



## Credenciais e Ambientes / Key Vault — 10/09/2026

| Verificação | Evidência desta revisão | Limite |
| --- | --- | --- |
| Restore / build Windows | `dotnet restore EsilvaSoft.SlopStudio.slnx --locked-mode`; build `--no-restore -p:UsedAvaloniaProducts=`; 0 avisos/erros | Restore precisou de acesso ao NuGet.Config do usuário; nenhuma dependência adicionada |
| NUnit Windows | **259/259 aprovados**, 0 ignorados | TRX: `tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/chuke_ESILVA-PC_2026-09-10_14_34_49_net10.0.trx` |
| Credenciais diretas e opcionais | Duas URIs válidas via parser MongoDB, round-trip LiteDB, Development/Production, caracteres reservados; campos de usuário/senha persistem URI direta | Autenticação contra servidor real pendente |
| Compatibilidade e recuperação | URI legada preservada após reinício, nenhuma migração de secrets; cofre inválido/versão futura não sobrescrito; falha de gravação visível | Criptografia/cofre nativo não implementados |
| Contexto e concorrência | Snapshot mantém valores anteriores; alteração invalida explorer, rejeita abertura antiga e preserva execução em andamento; entradas administrativas capturam ambiente | Retornos usam executores controlados |
| Editor | Valor ENV inserido como texto escapado, sem injeção de campos; ObjectId e Int64 máximo preservados; script com ENV e chave ausente | JSON estrito administrativo mantém suas validações; entrada `slop.input` é literal |
| Bootstrap JavaScript | Node executou o script gerado com stubs em dois ambientes; ordem conectar/banco/emitir, erro de chave ausente e remoção das variáveis de transporte conferidos | Não equivale a mongosh ou MongoDB real |
| UI | 18 PNGs de Ambientes: 2 temas × 3 tamanhos × 3 escalas, inspecionados; sobreposição corrigida e geometria testada; formulário de conexão claro/escuro inspecionado | PNGs em `tests/EsilvaSoft.SlopStudio.UnitTests/bin/Debug/net10.0/ui-evidence`; teclado Escape testado, leitor de tela pendente |
| Linux | Não reexecutado nesta revisão | Números Linux anteriores são baseline; homologação desta alteração pendente |

Cofre de ambientes usa armazenamento local sem criptografia nativa, separado dos snapshots. O aceite automatizado prova configuração simultânea e resolução por ambiente; homologação ponta a ponta exige MongoDB/mongosh instalados e uma fixture descartável, conforme o checklist. Nenhuma credencial real foi usada nesta validação.

## Database Explorer — 10/09/2026

Restore travado aprovado; build Windows com `--no-restore -p:UsedAvaloniaProducts=` aprovado, **0 avisos e 0 erros**. Suíte final: **276/276 testes aprovados**, nenhum ignorado. [TRX desta revisão](../tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/chuke_ESILVA-PC_2026-09-10_23_11_35_net10.0.trx). Os totais anteriores são históricos.

| Critério de aceite | Implementação e evidência automatizada | Homologação externa |
| --- | --- | --- |
| 1. Cadastrar/conectar | Formulário/repositório existentes, raízes salvas desconectadas; carga explícita testada | Autenticação MongoDB real pendente |
| 2. Expandir conexão | Bancos sob demanda; teste `SavedConnectionsAppearWithoutNetworkAndChildrenLoadProgressively` | Privilégios reais pendentes |
| 3. Expandir banco | Coleções sob demanda no mesmo teste; nenhuma consulta de documentos ao navegar | Catálogos reais pendentes |
| 4. Consultar documentos | Editor com filtros, sort, projection, limit e páginas; JSON e árvore estruturada renderizados | Paginação contra coleção real pendente |
| 5. Índices/detalhes | Índices compostos, direção, unique, sparse, TTL, partial e definição preservados em `IndexMetadataRetainsCompoundDirectionsAndAdditionalOptions` | Variações de versão MongoDB pendentes |
| 6. Criar/remover índices | Ferramenta gráfica acessada pelo menu real no teste de UI; remoção com contexto e proteção `_id_` testadas | Criação/remoção em servidor pendentes |
| 7. CRUD de documentos | Serviços existentes integrados; releitura completa, precondição e conflito em `EditLoadsFullDocumentAndUsesSnapshotPrecondition`; confirmação explícita | CRUD real e permissões pendentes |
| 8. Scripts CRUD | Namespace escapado; menu real gera sem executar; opções de índices em Extended JSON | Execução mongosh real pendente |
| 9. Menus contextuais | `ContextMenusGenerateScriptsWithoutExecutingAndRenderDocumentTree` abre o menu e aciona scripts/ferramentas | Teclado e leitor de tela nativos pendentes |
| 10. Instância/topologia | Adaptador de hello distingue primary, membros, arbiter e restrições; detalhes cancelam seleção antiga | Replica set, mongos e load balancer reais pendentes |
| 11. Selecionar instância | URI mantém autenticação/TLS; resolução SRV/TXT controlada testada; abas anteriores mantêm destino; recuperação sem conexão automática | DNS, failover e roteamento reais pendentes |
| 12. Múltiplas conexões | Todas as raízes salvas, contexto por aba; alteração de ambiente invalida conexões sem redirecionar execução | Sessão real com múltiplos servidores pendente |
| 13. Refresh granular | `RefreshRetainsExpandedNodesAndDoesNotReloadSiblingConnection`; resposta atrasada após desconectar rejeitada | Latência real pendente |

PNGs reais Headless/Skia inspecionados em **18 combinações** (claro/escuro × 960×620, 1366×768, 1920×1080 × escalas 1, 1,5, 2), além do editor de documento nos dois temas. Evidência em `tests/EsilvaSoft.SlopStudio.UnitTests/bin/Debug/net10.0/ui-evidence/explorer-*.png` e `document-editor-*.png`; prévias no [guia](19-database-explorer.md). Testes usam dados sintéticos e executores controlados. Linux, diálogos nativos, leitor de tela e MongoDB/mongosh reais não foram homologados nesta revisão.

## Console JavaScript — 11/09/2026

Restore com `--locked-mode` aprovado; build `--no-restore -p:UsedAvaloniaProducts=` com **0 avisos/erros**; **294/294 testes aprovados**, nenhum ignorado. [TRX final](../tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/chuke_ESILVA-PC_2026-09-11_07_55_19_net10.0.trx).

| Aceite do Console | Evidência |
| --- | --- |
| 1. Conexão principal selecionável | Destino usa os nós conectados por identidade; CanExecute/contexto por aba. ConsoleWorkspaceTests verifica isolamento entre perfis e retorno fora de ordem. |
| 2. Banco principal selecionável | Seletor de banco da conexão; ConsoleWorkspaceTests valida captura antes de alteração do banco e recuperação. |
| 3. db aponta ao banco selecionado | ConsoleMongoIntegrationTests executa db.Customers no CompanyDb e compara os três caminhos de consulta. |
| 4. getConnection acessa conexões cadastradas | Runtime real usa snapshots dos perfis; testes cobrem duas conexões, nome inexistente/ambíguo tratado pelo proxy e URI não utilizada inválida. |
| 5. ConnectionPool navega conexão/banco/coleção | Runtime tests exercitam propriedades e indexadores com espaços/hífens; integração real compara os documentos retornados. |
| 6. ENV.get funciona | Snapshot permanece estável durante alteração; integração real insere Name via ENV e consulta o documento correspondente. |
| 7. Autocomplete contextual | ConsoleWorkspaceTests cobre sugestões globais, conexões, bancos e coleções, indexadores e requisição assíncrona suspensa; view rejeita texto/destino antigos. |
| 8. Queries no painel | ConsoleUiTests executa JavaScript pelo editor e verifica dois resultados; PNGs reais inspecionados nos dois temas. |
| 9. console.log em Mensagens | Verificado no runtime, interface e fixture real; warn/error usam o mesmo canal com identificação. |
| 10. Cancelamento | Token atinge operação de banco controlada, abas permanecem independentes; loop limitado; confirmação negada por Escape não envia escrita. |
| 11. Histórico | Persistência LiteDB verificada para sucesso/erro/cancelamento, destino capturado e conexões usadas; opt-out não grava; metadados não incluem resultados/URI. |
| 12. Explorer gera nova sintaxe | Menu real gera Console sem execução; abrir coleção produz find limitado; scripts CRUD/índices usam o db da aba. |

Cobertura adicional: migração de rascunho JSON preservando opções e destino; proteção somente leitura aplicada à conexão adicional; rejeição de filtro vazio, _id_ e $out/$merge; seleção por parser sem fallback para script completo; objetos host não expostos. Testes anteriores do runner mongosh mantidos explicitamente nesse modo, sem relaxar sua política de somente leitura.

**MongoDB real:** ConsoleMongoIntegrationTests inicia dois processos Community **8.0.30**, apenas em 127.0.0.1, com diretórios próprios e dados sintéticos. Exercitados insertOne/Many, find/One, count/estimated, distinct, aggregate, sort/skip/limit/project, updateOne/Many, replaceOne, deleteOne/Many, create/dropIndex, múltiplas conexões e confirmação. Tipos ObjectId, UUID legado, Int64 máximo, Decimal128 e data conferidos no caminho real. Processos encerrados ao final; nenhuma conexão do usuário acessada. Binário portátil em .cache, fora da distribuição.

**Visual:** 18 PNGs Console (2 temas × 960×620/1366×768/1920×1080 × 100/150/200%) inspecionados, além das evidências regeneradas do Explorer/documentos. [Guia e prévias](20-console.md). Esta revisão não homologa Linux nativo, leitor de tela, TLS/autenticação, topologias distribuídas ou mongosh real.


## UUID/GUID configurável — 11/09/2026

Build Windows `--no-restore -p:UsedAvaloniaProducts=` com **0 avisos/erros**; **369/369 testes executados aprovados**, 1 ignorado (MongoDB real ausente). TRX final — `../tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/chuke_ESILVA-PC_2026-09-11_14_50_37_net10.0.trx` (artefato histórico ausente neste checkout; resultado preservado como registro, não revalidado). Linux não reexecutado nesta revisão.

| Aceite | Evidência automatizada | Validação externa |
| --- | --- | --- |
| Bytes por representação | `UuidCodecTests`: fixtures hexadecimais independentes para Standard, C# legacy, Java legacy e Go/Standard; cruzamento com `GuidRepresentation` do driver | Pendente: `ConsoleMongoIntegrationTests` grava os quatro construtores e confere bytes/subtype (ignorado sem `SLOP_CONSOLE_MONGOD`) |
| Ida e volta, zero, caixa, inválido | Construtor → BSON → saída → construtor, UUID zero, maiúsculas/minúsculas, sem hífens; inválidos com linha/coluna no codec, Console, adaptador e importação | — |
| UUID em string e subtype 3/4 | Strings, regex, comentários e acesso a membro intactos; subtype 4 nunca vira legado; subtype 3 sem perfil = origem desconhecida | — |
| Coleções mistas e consulta explícita | Documento misto em C#/Java; Console envia filtros com representação escolhida; integração real conta `CGUUID` 1 e `JUUID` 0 | Pendente com servidor real |
| Conexões simultâneas | `ConnectionsWithDifferentPoliciesRunConcurrentlyWithoutInterference`: duas abas, conclusão fora de ordem, snapshot capturado, mudança durante execução, global sem sobrepor conexão | — |
| Console, Documentos, clipboard, snippets | Metrics/Resultados/árvore, área de transferência Headless, editor de documento, “Abrir no editor” executado no Console com bytes idênticos, **Gerar UUID**, exportação canônica | Área de transferência nativa e leitor de tela pendentes |
| Migração e falha | Sessão sem campos → Standard; valor desconhecido visível e não sobrescrito; falha de gravação visível, aplicada em memória e repetível | — |
| mongosh | Helpers executados com stubs no Jint e comparados byte a byte com o codec | mongosh instalado pendente |
| UI | 54 PNGs `uuid-*`: Documentos, Preferências e editor de conexão × 2 temas × 3 tamanhos × escalas 1/1,5/2; geometria do seletor e do botão Salvar verificada; amostras inspecionadas | Gerenciadores de janela reais pendentes |

## Resultados JSON e árvore — 11/09/2026

Build Windows `--no-restore -p:UsedAvaloniaProducts=` com **0 avisos/erros**; **414 testes aprovados, 0 falhas, 1 ignorado** (MongoDB real ausente), total 415. Baseline imediatamente anterior: 369 aprovados e 1 ignorado. TRX final — `../tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/chuke_ESILVA-PC_2026-09-11_16_08_39_net10.0.trx` (artefato histórico ausente neste checkout; resultado preservado como registro, não revalidado). Linux não reexecutado nesta revisão.

| Aceite | Evidência automatizada | Validação externa |
| --- | --- | --- |
| Alternar JSON/árvore sem perder documentos, origem ou seleção | `SwitchingViewsKeepsDocumentsOriginAndSelectionAcrossResultSets`; teste de UI troca pelo seletor real e mantém o documento selecionado | — |
| Expandir/recolher documentos, objetos e arrays | `TreeExpandsObjectsAndArraysOnDemandWithNamesIndexesTypesAndValues`: carga sob demanda, 40 níveis, índices `[i]`, recolher sem recarregar | Árvores muito grandes sem virtualização |
| Campos ausentes, `null`, nomes com ponto, arrays vazios, estruturas profundas | Mesmo teste; formatador com 100 níveis; documento sem `_id` | — |
| Preservação de tipos | `ExtendedJsonPresentationTests`: fixture escrita à mão conferida pelo parser do driver; ObjectId, Int32, Int64 máximo, Double `-0.0`, Decimal128, data, binário, regex, timestamp e MinKey; UUID Standard, C# legacy, Java legacy, Go e Python legacy (subtype 3 de origem desconhecida) com bytes idênticos após reescrever os construtores | Fixture MongoDB real pendente |
| Documento único, múltiplos documentos, múltiplos conjuntos, truncado | Testes do painel e de UI com documento único aberto por padrão, três documentos, dois conjuntos, `limitado` e projeção parcial | — |
| Menu por mouse e teclado | UI: botão direito sobre o item, Shift+F10 e tecla Menu na árvore e no JSON (documento no cursor); motivo textual quando a edição está indisponível | Leitor de tela pendente |
| JSON somente leitura e cópia formatada | UI abre a modal pelo menu, confere origem, destino e texto, copia e fecha por Escape | Área de transferência nativa pendente |
| Edição: cancelar, alterado, removido, sem `_id` | `EditorOpensTheCopyWithoutIoAndRereadsTheDocumentBeforeWriting` (inalterado, alterado, removido), `ReadOnlyOrClosedConnectionOpensTheCopyButBlocksSaving`, `EditAvailabilityRequiresIdentityAndCompleteCollectionDocuments`; Escape na modal de UI | Gravação e conflito contra servidor real pendentes |
| Nada executado implicitamente | Chamadas ao serviço Mongo e ao runner contadas antes e depois de menus, visualização, edição e cancelamento | — |
| Isolamento entre abas e respostas fora de ordem | `TabsKeepTheirOwnResultsViewAndSelectionWhenResponsesArriveOutOfOrder`; troca de destino limpa só a aba; snapshot sem resultados | — |
| Ordem de campos do driver no Console | `ResultsKeepDriverFieldOrderAndReportProjectionUnlessTheScriptChangesThem` | — |
| Renderização | 72 PNGs: JSON e árvore (960 × 620, 1366 × 768, 1920 × 1080), modal JSON e edição (600 × 420, 760 × 560, 900 × 650) × claro/escuro × 100/150/200%; geometria do seletor, de Copiar JSON e das ações das modais; amostras inspecionadas | Gerenciadores de janela reais pendentes |

Asserções alteradas: `WorkspaceUiTests` e `UuidPreferencesUiTests` passaram a esperar `"campo": valor`, porque Resultados agora é indentado. A asserção negativa de vazamento entre abas usa o mesmo formato, para não passar trivialmente. Nenhum golden file foi alterado.
## Autocomplete local Qwen — 11/09/2026

| Gate | Evidência atual | Limite |
| --- | --- | --- |
| Restore/build | Restore travado e build Windows com `-p:UsedAvaloniaProducts=`, 0 avisos/erros | Opção evita somente a tarefa externa de telemetria Avalonia |
| Suíte regular | **415 aprovados**, 0 falhas; [TRX](../tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/autocomplete-current-final.trx) | Dois testes Qwen são Explicit e executados à parte |
| IA real CPU | **2 aprovados**; [TRX](../tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/qwen-current-final.trx) | Qwen2.5-Coder-0.5B Q4, CPU Windows x64; não generaliza qualidade/performance a modelos maiores |
| FIM/tokenizer | Tokenizer nativo + FIM gerou `a + b`; código sintético avaliado com resultado 5 | Teste fixo, não benchmark de qualidade |
| Cancelamento nativo | Cancelar geração longa, recuperar sessão e gerar novamente | Inicialização nativa não é imediatamente interrompível; worker mantém UI livre |
| Contexto/concorrência | AutocompleteReliabilityTests: 1 inferência ativa, fila cancelada não executa, duas sessões independentes; limite antes do runtime | Fakes determinísticos, complementados pelo teste nativo de cancelamento |
| Fallback/cache | LocalAutocompleteTests: AI/Auto → básico, cooldown, sem carga desabilitado, cache 64/expiração, ausência/invalidez, privacidade | Não promete reconhecer segredos arbitrários |
| Persistência/recuperação | Preferências antigas/defaults, round-trip, falha visível sem aplicar configuração não salva, sessão corrompida/futura não sobrescrita | Mesmo proprietário LiteDB, nenhuma segunda conexão |
| UI | Tab/Escape, preview multiline, 18 PNGs de editor + 18 de preferências + 2 Qwen reais inspecionados | Headless/Skia; leitor de tela e diálogos nativos não homologados |
| Publicação | Self-contained win-x64/win-arm64/linux-x64/linux-arm64; DLLs Windows x64/ARM64 corretas e sem pesos | Cross-publish não comprova execução em Linux ou hardware ARM64 |
| Aceleração | CPU distribuído; detecção/seleção DML/CUDA preparada, CPU fallback | DML/CUDA sem binários/homologação; OpenVINO/QNN/NPU não implementados |

Pesos de teste externos ao repositório; nenhuma dependência Python/Ollama/servidor. [Modelo, revisão, hash e receita de instalação](21-autocomplete-local.md#receita-reproduzível-validada-para-05b-q4).

## Autocomplete preditivo — auditoria de aceite (11/09/2026)

Esta revisão substitui as limitações anteriores de preview abaixo do editor e contexto somente textual. Restore `--locked-mode` aprovado; build `--no-restore -p:UsedAvaloniaProducts=` com 0 avisos/erros; **431 testes regulares aprovados**, 0 falhas ([TRX](../tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/predictive-final.trx)). **2 testes Explicit reais aprovados**, separados da suíte comum ([TRX](../tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/predictive-real-final.trx)).

| Requisito | Implementação e evidência conferida |
| --- | --- |
| Modelo preparado e reutilização | LocalModelCatalog + registro singleton de serviço/provider; modelo em `F:\models\Qwen2.5-Coder-0.5B-onnx-int4-cpu`; RealQwenGeneratesWithCpuAndReusesNativeSession gera, cancela e volta a gerar na sessão nativa |
| Carregamento desacoplado e UI responsiva | OnnxLocalModelRuntime executa carga/geração em Task.Run, provider serializa por SemaphoreSlim; real Qwen pelo editor com dispatcher ativo |
| Geração automática | EditorCompletionChanged observa texto/cursor/seleção; RealQwenCompletionTravelsThroughEditorAndAcceptsTab obtém `a + b` sem botão de sugestão |
| Ghost text sem editar documento | InlineCompletionTextBlock projeta prefixo/inserção/sufixo no layout real; PendingContextChangesAreRejectedAndGhostTracksTheCaretWithoutEditingTheDocument verifica documento intacto, sufixo, posição e rolagem |
| Tab incremental e Escape | IncrementalCompletion, testes por caminho/expressão/aspas/CRLF; teclado real Headless aceita partes, conserva resto e descarta por Escape; sem preview permanece o handler normal |
| Digitação, Backspace e contexto obsoleto | CompletionSession por editor com versão/CTS; testes de debounce, ABA, resposta tardia após mudar Input, Backspace, desabilitação, concorrência e cancelamento nativo |
| DSL e linguagem atuais | Context Builder usa ConnectionPool e getDatabase no Console, comandos mongosh em Script e operadores/stages em Agregação; CommandContextRespectsTheEditorLanguage |
| Dicionário antes de IA | DictionaryPrecedesModelAndDoesNotWaitForDebounce comprova resposta síncrona e zero carga ONNX; UI completa Conn para ConnectionPool |
| Input e Resultados temporários | ContextIsBoundedKeepsCursorAndHonorsIndependentOptions e ResultFieldsAndInputStayInsideTheirOriginatingTab; campos de documento real da fixture, sem valores, sem atravessar abas/destinos |
| Nomes e histórico | Snapshot copia nomes dos perfis, nós já carregados da conexão da aba, destino e até três comandos da mesma conexão/banco; não consulta MongoDB a cada tecla |
| Contexto limitado | Janelas de caracteres e limites auxiliares em AutocompleteContextBuilder/Bounded; orçamento final em tokens nativos no QwenFimPromptBuilder; testes de limite e preservação junto ao cursor |
| Fallback e cache | Ausência/invalidade/falha, cooldown e ausência de carga desabilitado; cache 64/30s com chave do request completo; CacheIncludesAuxiliaryContextAndDictionaryCanBeDisabled |
| Preferências | Modal existente ampliada; campos novos aditivos, round-trip dos cinco flags desligados, defaults de sessão antiga e erro de persistência sem aplicar configuração não salva |
| Inferência curta | 32 tokens padrão; sufixo existente interrompe geração e é removido da inserção; SuffixEchoIsNotInsertedTwice e integração real |
| Arquitetura/extensibilidade | Contratos e DI existentes preservados; nenhuma dependência ONNX em editor/Core; runtime substituível por fábrica, catálogo/tokenizer/prompt separados |
| Documentação | Guia 21, guia de uso, design system, ADR-030, plano, catálogo e acompanhamento atualizados |

Inspecionados PNGs reais de 18 combinações do editor e 18 das preferências (claro/escuro, três tamanhos, escalas 100/150/200%) e duas imagens de inferência Qwen. A projeção respeita o fundo do template focado e recorte do viewport. Evidência em `tests/EsilvaSoft.SlopStudio.UnitTests/bin/Debug/net10.0/ui-evidence/autocomplete-*.png`. Não se alteraram golden files. Asserções antigas de Tab integral foram substituídas por verificação de inserção parcial, resto disponível e reconstrução integral; testes de falha agora iniciam a IA com prefixo sem resposta óbvia do dicionário, preservando a verificação de cooldown/fallback.

Limites: execução comprovada em Windows x64 CPU. Headless/Skia não homologa leitor de tela, IME, gerenciador de janela nativo ou Linux/ARM64. 150 ms é debounce, não promessa de latência total; qualidade e duração variam conforme contexto/hardware. Resultados contribuem com campos de amostra limitada, não treinamento. Publicações descritas na seção anterior correspondem ao incremento anterior, não foram refeitas nesta revisão preditiva.


## Syntax highlighting MongoDB — 12/09/2026

Windows x64 / .NET 10.0.401 / Avalonia 12.1.2. Restore com --locked-mode aprovado, sem alterar dependências/lockfiles. Build com --no-restore -p:UsedAvaloniaProducts= aprovado, zero avisos/erros. Suíte completa com --no-build --no-restore: **453 aprovados, zero falhas**, em 34 s. O runner informou duas integrações Qwen opt-in não executadas; elas não integram a contagem de testes aprovados. TRX local: tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/syntax-validation.trx. Após o ajuste visual final do fundo focado, novo build aprovado e os **4 testes de UI de highlighting/autocomplete** passaram em 15 s.

| Cenário | Evidência |
| --- | --- |
| JSON, números, booleanos, null, propriedades/strings e Extended JSON | SyntaxHighlightingTests preserva texto e dígitos Int64, reconhece wrappers/construtores |
| Query API, pipelines e Atlas Search | Casos de find, $gte, $group/$sum, distinção de $set em update/pipeline, operadores Search apenas no contexto |
| DSL e namespaces | Caminhos ConnectionPool/ConnectionPull e getConnection; nomes conhecidos por caminho, fallback sem metadados e índice conhecido |
| Delimitadores e texto não executável | Pares excluem strings/comentários/regex e identificam delimitador sem par |
| Incremental e cancelamento | 5.000 linhas com edição local reutilizam sufixo; abertura/fechamento de comentário propaga estado; snapshots independentes |
| UI e documento extenso | Trabalho obsoleto cancelado; 4.000 linhas com spans de região distante limitados; não comprova virtualização do TextBox |
| Temas, seleção e undo | Troca Dark/Light reutiliza snapshot, contraste >= 4,5:1 para todos os tokens, seleção preservada, edição e Ctrl+Z reais Headless |
| Visual | **36 PNGs reais inspecionados**, workspace 960/1366/1920 e modal 600/760/900, escalas 100/150/200%, ambos os temas; fundo focado final revalidado |
| Regressões | Suíte existente de autocomplete/resultados e demais jornadas aprovada; sem alteração de golden files |

Prévias duráveis: [claro](ui/syntax-claro.png), [escuro](ui/syntax-escuro.png). Arquitetura, tokens, extensão e limitações em [22](22-syntax-highlighting.md). A classificação não executa MongoDB nem altera BSON/sessão. Homologação Linux, leitor de tela, IME nativo e MongoDB/mongosh real não foi realizada nesta entrega. O TextBox continua medindo todo o documento; não há promessa de responsividade constante para arquivos ilimitados.

## Modos de identificador — 12/09/2026

Build Windows `--no-restore -p:UsedAvaloniaProducts=` com **0 avisos/erros**; **504 aprovados, 1 falha, 1 ignorado**. TRX — `../tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/identifier-mode-final.trx` (artefato histórico ausente neste checkout; resultado preservado como registro, não revalidado). A única falha (`HugeSingleLineKeepsFullTextAndMakesEveryRangeReachableWithoutShapingItAll`, syntax highlighting) reproduz no commit `f8e7a16` sem estas alterações. Linux não reexecutado.

| Aceite | Evidência automatizada | Validação externa |
| --- | --- | --- |
| Opções ObjectId, UUID v4 e Standard; Standard ≠ UUID v4 | `ModesAreThreeNamedValuesAndStandardMeansObjectIdPlusUuid`: três valores serializados por nome, sem `StandardUuid`, rótulos e descrições por modo | — |
| Standard aceita e preserva ObjectId e UUID | `BsonTypeHasPriorityAndLookalikeStringsStayStrings`, `HumanJsonUsesConstructorsForBothTypesAndRestoresIdenticalBson` (três modos, BSON idêntico após reescrita), strings com cara de identificador intactas | — |
| ObjectId → UUID determinística, sem alterar BSON | Fixture independente `66e3bd5b-3bc3-f54c-840d-73ac00000000`, reversão e caixa; `ModeChangeRerendersResultsButEditorCopiesAndExportKeepTheStoredTypes`: texto do editor, gravação (`BsonType.ObjectId`), precondição e exportação canônicas | Pendente com servidor real |
| UUID subtype 4, CGUUID, JUUID, GUUID | Suítes UUID existentes aprovadas; parsing comparado a `BsonBinaryData` do driver; placeholders `CGUUID`/`JUUID` | — |
| Configuração separada e migração sem perda | Sessão sem `IdentifierMode` com `UuidRepresentation` Standard e JavaLegacy → modo Standard e representação preservada; valor desconhecido visível e não sobrescrito; falha de gravação visível | — |
| Parsing e formatação centralizados | `ParseIdentifierAcceptsRequiredFormsInEveryModeWithDriverIdenticalBson`, inválidos com motivo e linha/coluna; UI, adaptador, Console e importação chamam o serviço | — |
| Visualizadores, cópias e scripts | Árvore, identidade, Documentos, JSON, editor, `EJSON.parse` do Console, importação, `_id` de exemplo do Explorer, **Gerar identificador**, **Interpretar**; menu com cópia de `_id`, consulta e UUID equivalente via área de transferência Headless | Área de transferência nativa pendente |
| Preferências e prévia | `IdentifierModesDriveResultsMenuAndPreferencesAndRenderInEveryRequiredVariant`: descrição, seções ObjectId/UUID e comparação das quatro formas por modo; 18 PNGs `identifier-results-*` e 54 `identifier-preferences-<modo>-*` × claro/escuro × 3 tamanhos × 100/150/200%; geometria do seletor e da descrição verificada | Leitor de tela e gerenciadores de janela reais pendentes |

## ONNX SlopCoder, hardware e chat — 13/09/2026

Windows x64, .NET 10.0.401: restore `--locked-mode` e build `--no-restore -p:UsedAvaloniaProducts=` aprovados nas variantes Cpu, WinML e Cuda (0 avisos/erros). Suíte final padrão WinML: **520 aprovados, 1 falha** em 37 s; [TRX](../tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/onnx-expanded-final.trx). A falha é `HugeSingleLineKeepsFullTextAndMakesEveryRangeReachableWithoutShapingItAll`, já registrada na entrega anterior de identificadores; asserção e golden files não foram alterados. Seis testes nativos Explicit ficam fora da suíte regular.

| Verificação | Resultado e evidência |
| --- | --- |
| Tokenizer e prompt DeepSeek | 547 strings e 40 prompts completos com IDs idênticos; decode também confere os três casos sem round-trip do HF. [TRX CPU](../tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/deepseek-cpu-final.trx) |
| SlopCoder real CPU | Geração repetida, cancelamento e recuperação; 12 tokens em 1168/1146 ms em prompt curto sintético. Mesmo TRX; não é TTFT nem benchmark do editor |
| Chat e GPU → CPU | Proposta ONNX real e recuperação depois de erro DirectML aprovadas. [TRX](../tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/deepseek-chat-fallback.trx) |
| GPU estrita | Falhou na execução do pacote CPU (`DmlFusedNode_0_4`, `80070057`). [TRX](../tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/deepseek-gpu.trx). Não há homologação GPU deste pacote; CUDA só restaurado/compilado |
| Contratos/falhas | Manifesto com ID incorreto e pesos externos ausentes retornam Invalid; propostas truncadas e marcadores são rejeitados; chat cancelado não cancela autocomplete em fila; sessão inicializada uma vez; filtros de privacidade impedem carga |
| Regressão visual | Suíte Headless produziu PNGs existentes de autocomplete; inspecionados claro/escuro 1366×768 100%, ghost text e painel IA. Não houve mudança de layout/XAML |

O chat FIM gerou filtro de data adicional não solicitado: integração funcional não representa aprovação de fidelidade conversacional. Revisão e confirmação permanecem obrigatórias. Linux, GPU com exportação compatível, leitor de tela, diálogos nativos e MongoDB real não foram homologados nesta entrega. [Guia e contratos](23-onnx-slopcoder.md).

Regressão Qwen real no build WinML: **1 aprovado**, geração CPU repetida e recuperação de cancelamento; [TRX](../tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/onnx-qwen-regression.trx).


## Datas BSON — 13/09/2026

Restore locked aprovado; build sem restore com -p:UsedAvaloniaProducts= aprovado, 0 avisos/erros. Suíte final: **530 aprovados, 1 falha preexistente** em HugeSingleLineKeepsFullTextAndMakesEveryRangeReachableWithoutShapingItAll (já registrada na entrega ONNX). TRX: tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/date-format-final.trx.

Novos testes verificam bytes BSON com fixture do driver, UTC e offset -03:00, milissegundos negativos/zero, datas inválidas, limite Int64, strings preservadas, argumentos BSON enviados pelo Console e helper mongosh executado em Jint. Resultado visual confirmado na modal de documento: Date legível nos PNGs reais result-document-json-Light-760-1.png e result-document-json-Dark-760-1.png em ui-evidence do diretório de testes. Os testes existentes de resultados/cópia e representações também passaram.

Não homologado nesta alteração: MongoDB/mongosh real, Linux, leitor de tela e área de transferência nativa. Exportação permanece canônica.

## Revisão documental do roadmap — 13/09/2026

Inspeção estática do checkout `e806ae4`, serviços concretos, chamadas de UI, testes, dependências e tags locais. [Inventário](24-inventario-roadmap.md) associa status a código e lacunas; [roadmap](09-plano-de-implementacao.md) substitui F0–F7 por seis versões. Não houve execução de restore/build/NUnit, MongoDB, mongosh, modelos ou homologação visual nesta tarefa, que altera somente Markdown. As contagens de testes anteriores permanecem históricas, inclusive a falha registrada; não são o resultado do checkout atual.

Verificação documental: 68 requisitos originais preservados e EDT-08 acrescentado; status e versão nas 69 definições; seis fases com objetivo, escopo, exclusões, aceite, dependências e documentação. Links locais, tabelas e blocos de código conferidos. Três links antigos para TRX ausentes foram convertidos em referências textuais explícitas, preservando o registro sem oferecer download inexistente. Referências comparativas removidas conforme o escopo; não houve auditoria jurídica de terceiros nem alteração de avisos/licenças.

## Homologação nativa Windows — 14/09/2026

| Cenário | Evidência | Estado |
| --- | --- | --- |
| Conectar e navegar | Perfil `MvpNative` em MongoDB 8.0.30 local; databases `admin`, `config` e `local` carregados após expansão | ✅ Homologado |
| Consulta e barra inferior | `system.version` retornou 1 documento; barra mostrou execução, cancelamento disponível e conclusão | ✅ Homologado |
| Exportação JSON | Seletor nativo salvou Extended JSON válido | ✅ Homologado |
| Exportação CSV | Seletor nativo salvou cabeçalho `_id,version` e linha `featureCompatibilityVersion,8.0`; extensão digitada prevalece sobre filtro inicial | ✅ Homologado |
| Cancelamento de consulta | Ação **Cancelar** da barra inferior acionada durante execução; IDE retornou a `Pronto` e informou que efeitos já enviados não são revertidos | ✅ Homologado |
| Cancelamento de exportação | Testes de streaming cancelam após escrita parcial, propagam `CancellationToken` e removem o arquivo incompleto | ✅ Homologado |
| Linux visual | Validação visual dispensada por decisão de escopo | ⏭️ Fora do aceite |
| Clipboard Windows | Botão **Copiar JSON** retornou o documento completo no clipboard nativo, sem credenciais | ✅ Homologado |
| Leitor de tela/cold start CPU-RAM | Leitor de tela não executado; startup/CPU-RAM possui uma amostra nativa | ⚠️ Parcial |
| Medição de inicialização Windows | 1.396 ms até janela; 220,3 MiB e 3.734,4 ms de CPU após 2 s, uma amostra sem inspeção visual | ⚠️ Medição inicial |

## Polimento MVP — 13/09/2026

Restore locked e build sem restore com `-p:UsedAvaloniaProducts=` aprovados, zero avisos/erros. Suíte `mvp-polish.trx`: **549 testes executados/aprovados, zero falhas**; 555 descobertos, seis casos explícitos de IA fora desta execução/MVP. Arquivo local: `tests/EsilvaSoft.SlopStudio.UnitTests/TestResults/mvp-polish.trx`.

Inclui dois testes de integração com MongoDB portátil 8.0.30 em Windows, paginação/CRUD protegido/conflito/exportação, 18 PNGs da barra concorrente nos temas/tamanhos/escalas declarados, undo real de formatação e correção do teste de linha com dois milhões de caracteres antes falho. Nenhuma asserção de limite visual foi enfraquecida. Startup Headless quente: 9 ms na última execução, 464 ms em execução anterior isolada; cenário de duas páginas de 100 em 5.000 documentos com edição/conflito/exportação: 55 ms (anterior 84 ms). Esses números são observações locais, não benchmark de produção.

Não encerrados: Linux gráfico, diálogos nativos/clipboard, leitor de tela, cold start/CPU/RAM nativos e matriz ampliada de autenticação/topologia. WSL sem distribuição disponível; acesso Linux solicitado. Veja a [auditoria completa e o inventário anterior às alterações](25-auditoria-mvp-performance.md). Não declarar v0.5.0 pronta por esses testes.

## IA local multimodelo — 13/09/2026

| Critério | Evidência |
| --- | --- |
| Suíte regular | **564 aprovados, 0 falhas, 2 ignorados**; `multimodel-regular.trx` em TestResults do projeto de testes |
| Diretório, descoberta e validação | `DiscoveryUsesFolderNamesOptionalMetadataAndIsolatesInvalidFolders`: nomes de pasta, metadata opcional, `MissingFiles`, `Invalid` e `Unsupported` sem afetar os válidos |
| Seleção e persistência | `SelectedModelIsAFolderNameThatCannotEscapeTheDirectory`, `SelectionResolvesInsideTheDirectoryAndChatCanUseItsOwnModel`, `PreferencesStoreFolderNamesAndRefreshKeepsTheSelection`, round-trip em LiteDB |
| Hardware e fallback | `AutomaticTriesNpuGpuThenCpuAmongAvailableAndCompatibleDevices`, `ExplicitHardwareUsesOnlyThatBackendAndExplainsUnavailability`, `ProbeOffersOnlyProvidersTheRuntimeReportsAndPrefersTheFirstAdapter` |
| Serviço central | Troca com descarga prévia, capacidade de chat, preempção do autocomplete, carga não abortada pelo editor, barra de atividades, teste de modelo e falha de provider |
| Real nesta máquina (WinML, Ryzen 9 7900, RX 7800 XT) | Sonda, teste de modelo CPU e Automático e geração CPU aprovados com SlopCoder-Mongo-1.5B; GPU explícita falhou na geração DirectML e reportou sem fallback. `multimodel-real.trx` |
| Não homologado | NPU, CUDA, Linux, diálogo nativo de pasta, uso interativo com MongoDB real; GPU para este pacote |

[Especificação e evidência](26-ia-local-multimodelo.md#evidência--13092026).


## Revisão do plano de autocomplete — 15/09/2026

Revisão documental, sem alteração de código de produto: inspeção estática e pesquisa técnica; validação de links/índice offline registrada ao concluir. Nenhum build/teste de produto, MongoDB real, modelo/CPU/GPU/NPU, teclado/IME ou PNG novo é alegado. Fase 1 continua com aceite parcial; schema learning, quatro providers, flags/atalhos e novos gates de concorrência/persistência/UI estão planejados, com casos em execution-plan/testing. [Plano revisado](auto-complite/README.md), [tarefas por agente](auto-complite/execution-plan.md) e [schema learning](auto-complite/schema-learning.md).

## Autocomplete tradicional — fechamento de lacunas (17/09/2026) — CONCLUÍDO

> Encerrada por decisão do responsável com pendências aceitas em aberto. As linhas marcadas como pendentes abaixo
> **não foram verificadas** e não devem ser lidas como aprovadas.

Build `dotnet build EsilvaSoft.SlopStudio.slnx --no-restore`: **0 avisos, 0 erros**.
Testes `dotnet test EsilvaSoft.SlopStudio.slnx --no-build --no-restore`: **1 156 aprovados, 0 falhas** (entrada: 1 127).

| Área | Evidência local | Validação pendente |
| --- | --- | --- |
| Lex único por requisição | `ShapeWalkerTokenSourceTests.WalkUsesTheTokensItReceivesInsteadOfLexingAgain` | — |
| Estreitamento em pipeline nu | `ShapeWalkerTokenSourceTests.RootShapeAppliesOnlyWhenThereIsNoEnclosingCall` | — |
| Vírgula devolve o quadro da chave | `ShapeWalkerTokenSourceTests.CommaReturnsToTheEnclosingObjectShape` | — |
| Acesso por gatilho (`Invoked` carrega, digitar não) | `TraditionalCompletionIntegrationTests`, testes do motor | — |
| Campos do pipeline por `LocalSchemas` | `PipelineStageReader` + testes de contexto | Homologação com coleção real |
| Descarte de resposta obsoleta e CTS por aba | `TraditionalCompletionIntegrationTests` | — |
| Sinal de uso ponta a ponta | `TraditionalCompletionUsageSignalTests` (6 testes) | — |
| Isolamento do núcleo (sem IA, sem rede, sem UI) | `AutocompleteArchitectureTests` (6 testes, agora varrendo todos os subespaços) | — |
| Alocação ≤ 64 KB por tecla, catálogo embarcado | `NameTableAllocationTests`; ver [performance](auto-complite/performance.md) | — |
| **Alocação ≤ 64 KB por tecla, campos de metadados** | **estourado: 174,72 KB a partir de ~1 000 campos** | **`MetadataCatalogSource.Describe` por candidato; não corrigido** |
| **Latência p95/p99 dos gates da Fase 2** | **nenhuma** | **Job completo do BenchmarkDotNet; não concluído nesta sessão** |
| `TypeMismatchPenalty` | nenhuma | Exige propagar `ApplicableTypes` ao `CompletionItem` |
| Elo `Stage → GroupBody` | nenhuma | `ShapeWalker.Match` ignora `Exclusive`/`rule.Names` |
| Matriz de 18 PNGs (claro/escuro, 4 estados) | nenhuma | Fora do escopo desta entrega |
| Highlighting em 64 KiB | 3,5 ms medidos contra orçamento de 2 ms | Não corrigido |
| MongoDB real, leitor de tela, diálogos nativos | nenhuma | Homologação manual pendente |
