# Registro de decisões arquiteturais

O quadro inicial preserva decisões de fundação; ADRs datadas registram revisões aceitas e propostas específicas. Para implementação atual, consultar o inventário e a matriz. A ADR-035 substitui o calendário F0–F7 pelo roadmap v0.5.0–v1.0.0; referências antigas de prazo não são compromissos vigentes.

| ADR | Decisão | Motivo e consequência |
| --- | --- | --- |
| ADR-001 | C#/.NET 10 e Avalonia desktop para Windows/Linux | Requisito fixo; CI e homologação nos dois sistemas |
| ADR-002 | MVVM e monólito modular | Testabilidade com implantação desktop simples |
| ADR-003 | Driver oficial e BsonDocument | Preservar tipos e aceitar documentos heterogêneos; não impor POCO do usuário |
| ADR-004 | Contratos de recursos MongoDB específicos | Evitar abstração CRUD que oculta índices, sessões e comandos |
| ADR-005 | Catálogo versionado de capacidades | Suporte varia por versão, FCV, permissão e serviços |
| ADR-006 | Editor textual MQL/JSON e modo script JavaScript + JSON via mongosh | Consultas são escritas em uma única superfície com autocomplete; script combina lógica e consultas na mesma execução; runtime externo e resultados EJSON; consolidação v0.8.0, com runner já existente |
| ADR-007 | Autocomplete determinístico local | Disponível offline, sem remessa de dados; IA é extensão opcional |
| ADR-008 | UUID binário explícito por valor/campo; detalhada pela ADR-028 | Não mudar serializer global quando a conexão muda |
| ADR-009 | Credenciais diretas e referências opcionais; revisada pela ADR-024 | Módulo local de ambientes; cofre de SO permanece planejado |
| ADR-010 | Streaming e jobs com estado de certeza | Limitar memória e não prometer rollback ao cancelar |
| ADR-011 | Exportação lógica e backup separados | Recuperabilidade exige metadados, consistência e restore testado |
| ADR-012 | API Atlas em adaptador próprio | Plano de controle e credenciais distintos do driver |
| ADR-013 | NUnit em todos os projetos de teste | Requisito fixo e integração com headless Avalonia |
| ADR-014 | MIT para código próprio | Atender licença solicitada e manter avisos de terceiros |
| ADR-015 | LiteDB local com migrações e processo proprietário | Requisito fixo; coleções de workspace/jobs e DTOs separados de BSON MongoDB |
| ADR-016 | Sem AOT/trimming como requisito inicial | Primeiro provar integridade com UI, serialização e bibliotecas nativas |

## Licenciamento e publicação

A raiz contém [LICENSE](../LICENSE), com copyright 2026 EsilvaSoft. O identificador do projeto e de futuros pacotes será `MIT`. Código e documentação próprios serão distribuídos sob essa licença. [Texto MIT](https://opensource.org/license/mit).

O núcleo Avalonia e AvaloniaEdit possuem licença MIT; produtos comerciais de tooling/controles não são automaticamente cobertos por ela. Não adicionar XPF, controles comerciais ou dependência de IDE paga sem decisão própria de produto. [Licença Avalonia](https://github.com/AvaloniaUI/Avalonia/blob/main/licence.md), [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit).

O driver MongoDB tem licença própria Apache-2.0, que deverá constar no inventário e nos avisos de distribuição. A MIT do aplicativo não relicencia driver, servidor, bibliotecas nativas, ferramentas MongoDB, fontes ou gramáticas de editor. A implementação deverá inventariar cada versão efetivamente empacotada. [Licença do driver](https://github.com/mongodb/mongo-csharp-driver/blob/main/LICENSE.md).

A fundação define formato de `THIRD-PARTY-NOTICES` e SBOM; A v1.0.0 exige os arquivos a partir das dependências reais. Database Tools e mongosh serão inicialmente localizados na instalação do usuário. Não incluir binários de servidor na distribuição da IDE por conveniência de testes.

LiteDB também possui licença MIT; registrar a versão efetivamente usada no inventário. A validação do pacote com .NET 10 em Windows/Linux pertence à fundação e à homologação de release. [Repositório LiteDB](https://github.com/litedb-org/LiteDB).

## ADRs da revisão UI/UX — aceitas em 10/09/2026

| ID | Decisão | Consequência |
| --- | --- | --- |
| ADR-017 | Bancos no explorer; conexões em modal; editor textual/resultados em divisão vertical | Substitui o layout anterior, prioriza a área de edição e elimina o construtor de consulta com múltiplos campos |
| ADR-018 | Recursos semânticos claros/escuros, acento azul, Inter compacta | Temas dinâmicos; texto sobre acento definido por tema; sem cores locais arbitrárias |
| ADR-019 | Contexto e cancelamento próprios de cada aba | Retornos não atravessam abas; banco/coleção são contexto da aba, não campos repetidos da consulta; perfil alterado exige reabertura |
| ADR-020 | Snapshot local de versão 1 com opt-out e entrada JSON opt-in | Migração aditiva no proprietário LiteDB; sem resultados/conexões restauradas; falha visível |
| ADR-021 | Reutilizar ferramentas e editor textual; validar com Headless/Skia | Preserva capacidades; consultas não usam formulário de filtro/ordenação/destino; não antecipa tabela/árvore nem promete validação nativa completa |

### ADR-023 — Consulta editor-first (aceita em 10/09/2026)

O editor textual é a única superfície para montar e revisar consultas. Filtro, projeção, ordenação, limite, paginação, hint, collation, `maxTimeMS`, `batchSize`, comentário e demais opções devem ser expressos no texto MQL/JSON, com autocomplete local, snippets e diagnóstico contextual. O banco e a coleção são escolhidos pelo contexto da aba, normalmente originado no explorer; não são campos duplicados dentro do modo de consulta.

Não haverá um modo de consulta composto por vários campos para filtro, ordenação, banco e opções. Formulários permanecem disponíveis para conexões e ações administrativas destrutivas, onde confirmação e proteção contra enganos são necessárias. O editor continua sendo a fonte de verdade de consultas salvas, histórico, execução e reprodução.

Especificação e critérios: [design system](17-design-system-ui-ux.md).


## ADR-022 — Identidade vetorial azul/violeta (aceita em 10/09/2026)

Adotar o recipiente inclinado com líquido das referências locais como símbolo simplificado. Geometrias próprias em Brand.axaml alimentam a UI e o gerador SVG/PNG/ICO; nenhuma dependência gráfica comercial ou nova biblioteca no desktop. Usar recursos semânticos dinâmicos, logotipo compacto, nomes acessíveis e ícones acompanhados por texto. A paleta operacional ajusta contraste em relação aos mockups. Nome técnico EsilvaSoft.SlopStudio e MIT preservados. [Manual, assets e evidências](18-identidade-visual.md). A validação atual é Windows/Headless; Linux e shell nativo exigem nova homologação.



## ADR-024 — Credenciais diretas e ambientes opcionais (aceita em 10/09/2026)

Revisa a ADR-009: permitir URI com senha literal e URI com `${ENV.get("chave")}` lado a lado. O módulo Ambientes / Key Vault mantém Development, Staging, Production e ambientes customizados; cada um tem seu dicionário e a seleção ativa é persistida. O rótulo de ambiente do perfil permanece metadado independente.

Persistência aditiva: coleção `environmentVault`, documento `current`, JSON versão 1, no proprietário LiteDB registrado em DI. Falha/versão desconhecida impede sobrescrita; não há migração de secrets para URI. O armazenamento é local, sem criptografia por cofre nativo; integração de SO permanece planejada.

`OperationEnvironment` captura um dicionário imutável e o ambiente legado antes de awaits. Driver e runner resolvem URI e editor com o mesmo contrato. Helpers do driver usam `AsyncLocal` por operação assíncrona, sem trocar os valores durante awaits. `ENV.get()` prioriza o ambiente ativo e permite fallback ao processo; `${NOME}` mantém a semântica antiga, sem nova codificação. Novas interpolações de componentes URI são codificadas; valores JSON entram como literais escapados, sem reprocessamento de código ou conversão de tipos BSON.

O runner recebe URI e valores pelo ambiente do processo filho; `--norc --nodb --file` mantém segredos fora dos argumentos e do arquivo temporário gerado. O bootstrap conecta e só então seleciona o banco da aba. Referências: [scripts mongosh](https://www.mongodb.com/docs/mongodb-shell/write-scripts/) e [opções do shell](https://www.mongodb.com/docs/mongodb-shell/reference/options/). Homologação com MongoDB/mongosh reais não é substituída por teste do bootstrap com stubs.

Salvar ambientes invalida o explorer e marca abas como desconectadas; reabrir é uma ação explícita. Operações iniciadas continuam com seu snapshot. Uma abertura que termina após a troca é rejeitada. Snapshots de rascunhos continuam sem credenciais do perfil, valores de ambientes ou resultados.


## ADR-025 — Database Explorer contextual (aceita em 10/09/2026)

Exibir todas as conexões cadastradas como raízes, com estado desconectado inicial. Carregar bancos, coleções e índices progressivamente; seleção consulta somente metadados. Refresh reconcilia nós existentes e invalida respostas antigas. Menus operam sobre o item apontado, sem alterar silenciosamente o contexto do editor. Detalhes ficam numa região recolhível do explorer; resultados ganham inspeção estruturada da página e ações por documento.

Separar nós, detalhes e operações de documento em viewmodels próprios, com `IExplorerMetadataService` na aplicação e adaptadores de BSON/roteamento na infraestrutura. Destino explícito de instância é contexto runtime, sem reescrever a URI. DNS/TXT SRV é resolvido assincronamente pelo driver antes de aplicar o destino. O campo opcional `TargetHost` é aditivo ao snapshot versão 1 e conserva o contexto após reinício, sempre desconectado.

Documentos projetados são relidos por `_id` antes de edição/exclusão; `_id` mais comparação de `$$ROOT` com o documento revisado impedem substituir/excluir uma versão diferente. Scripts serializam nomes e Extended JSON, sem execução automática. Abas geradas a partir de resultados recebem `ContainsResultData` e são excluídas do autosave pelo repositório; salvar arquivo continua explícito. Sem nova conexão LiteDB, dependência comercial ou alteração de licença. Jornadas, limites e evidência: [Database Explorer](19-database-explorer.md).

## ADR-026 — Console com proxies controlados (aceita em 11/09/2026)

Substituir Consulta JSON por Console como modo principal. Jint 4.16.0 (BSD-2-Clause) interpreta JavaScript em thread de trabalho, sem expor CLR; Acornima identifica statements e expressões para execução parcial e resultados. Proxies de conexão/banco/coleção/cursor encaminham operações tipadas a IConsoleDatabaseSession; o adaptador MongoDB.Driver valida método, política, BSON e limites antes do envio.

Contexto principal por aba é conexão/banco. getConnection e ConnectionPool usam snapshots dos perfis/ambiente; cada execução possui clientes e cancelamento próprios. Cursor materializa apenas uma página limitada, inclusive toArray. Toda escrita exige confirmação do destino real e auditoria; pipeline de leitura não aceita $out/$merge. Resultados carregam origem para evitar edição no namespace da aba quando a consulta veio de outra conexão.

Histórico aditivo consoleHistory versão 1 no proprietário LiteDB existente, com preferência de persistência, teto de 500 entradas e falhas visíveis. Rascunhos legados são convertidos em texto Console sem sobrescrever o arquivo original; resultados e credenciais continuam fora do autosave. O executor mongosh permanece separado, sem alteração de seus limites. [Guia e limites](20-console.md).

## ADR-027 — Autocomplete Qwen local com fallback (11/09/2026)

Revisa a extensão opcional da ADR-007: adotar ONNX Runtime GenAI 0.15.2 na infraestrutura para Qwen2.5-Coder exportado com tokenizer/FIM. Reutilizar Core/Application, sem novo domínio paralelo ou servidor. CPU é a distribuição mínima; DML/CUDA têm seleção preparada, NPU/OpenVINO/QNN não implementados. Pesos externos não integram Git/publicação. Preferências aditivas têm versão 1; requests não carregam perfis/ENV resolvido. Preview abaixo do TextBox e menu existente, Tab/Esc com prioridade de sugestão, cancelamento e versão próprios por editor. [Detalhes, privacidade e evidências](21-autocomplete-local.md).

## ADR-028 — UUID/GUID configurável por preferência e conexão (aceita em 11/09/2026)

Detalha a ADR-008. `UuidCodec` no Core concentra ordem de bytes, subtype, validação de texto, reescrita dos construtores `UUID`, `CGUUID`, `JUUID` e `GUUID` para Extended JSON canônico e transformação de saída humana. `GUUID` é alias Go/Standard: subtype 4 e bytes RFC 4122 idênticos a `UUID`; C# e Go usam texto maiúsculo, Java e Standard minúsculo. A capitalização é apresentação; nunca muda bytes.

Construtores são explícitos e independentes da preferência, o que permite consultas que escolhem a representação em coleções mistas. A preferência controla somente saída humana e snippets gerados: Resultados, árvore, área de transferência, editor de documento, “Abrir no editor” e **Gerar UUID**. Subtype 3 sem perfil legado explícito é exibido como binário de origem desconhecida. JSON canônico permanece fonte de `_id`, precondições, exportação e importação; não há alteração do serializer global nem migração de dados.

Preferência global e dicionário de sobrescritas por conexão são aditivos a `WorkspacePreferences` (snapshot versão 1), serializados como texto; sessões antigas recebem Standard. Valor desconhecido torna a sessão ilegível, fica visível e não é sobrescrito. Sobrescrita vive nas preferências, não no perfil, para não invalidar o explorer. Falha de gravação é exibida e a escolha permanece somente em memória.

Cada aba recebe `UuidDisplayPolicy` imutável; a execução captura o snapshot antes do primeiro await e resolve a representação pela conexão de origem de cada resultado. Mudanças durante a execução são aplicadas ao concluir; resultados ociosos são renderizados de novo a partir do canônico. O Console expõe os construtores por delegados de host removidos do escopo global; o runner mongosh recebe helpers JavaScript sobre `BinData`/`UUID` nativos, verificados byte a byte contra o codec. [Regras e evidência](06-editor-bson-e-uuid.md#representação-configurável--implementada-em-11092026).

## ADR-029 — Resultados estruturados com visualização JSON e árvore (aceita em 11/09/2026)

O painel Resultados consome `StructuredResultSet` e `StructuredResultDocument` (Core): conjunto, posição, JSON original, origem (fonte, perfil capturado, banco e coleção), `_id` bruto, truncamento e completude (`Complete`, `PartialProjection`, `Derived`, `Unknown`). Console, consulta, agregação e script produzem o mesmo modelo; Documentos, exportação e autocomplete continuam lendo o conjunto selecionado. O modelo vive só em memória e nunca entra em `WorkspaceDraft`.

A apresentação não desserializa tipos. `ExtendedJsonFormatter` altera apenas espaços fora de strings e copia cada token; wrappers como `$oid`, `$date` e `$binary` ficam em uma linha. `ExtendedJsonValue` nomeia tipos BSON a partir dos wrappers, mantendo os dígitos de Int64, Decimal128 e Double; UUID continua em `UuidCodec`. O texto indentado deriva do canônico, que segue como fonte de identidade, precondição e exportação.

O runtime do Console guarda a resposta do driver para find, findOne e aggregate (até 8 MB por execução) e a usa quando o valor devolvido pelo script é equivalente ignorando a ordem de campos. Assim, chaves numéricas não são reordenadas pelo JavaScript. Um valor alterado pelo script conserva a saída do script. A origem informa projeção: `find` com segundo argumento ou `project()` não vazios e `findOne` com projeção.

O menu contextual age no documento. Visualizar usa só memória. A edição abre uma cópia sem I/O e fica disponível apenas para leitura completa de coleção com `_id`. Salvar exige confirmação, relê por `_id`, compara com ordem de campos significativa, informa documento alterado ou removido sem gravar e, se igual, grava com a precondição `$$ROOT` existente. **Documentos → Editar…** mantém a releitura na abertura, que permite editar resultados projetados. Visualização, seleção e expansão ficam por aba durante a sessão; nova execução ou troca de destino limpa somente a aba correspondente. [Guia](14-guia-de-uso.md#resultados-json-e-árvore) e [matriz](15-matriz-de-validacao.md).

## ADR-030 — Autocomplete preditivo com contexto de sessão (11/09/2026)

Revisa ADR-027: o dicionário tem prioridade, evitando inferência/debounce em continuações óbvias. AutocompleteContextBuilder recebe snapshot da aba e combina janela do editor, APIs reais, Input opcional, campos dos resultados e histórico limitado; nenhum perfil completo, ENV resolvido ou valor de resultado é enviado. Os dados ficam em memória, com opções independentes e migração aditiva das preferências versão 1. Cache inclui todo o contexto.

O TextBox permanece como proprietário do documento e do undo. InlineCompletionTextBlock projeta ghost text no layout do TextPresenter, com sufixo deslocado visualmente e recorte do viewport. IncrementalCompletion aceita identificadores/caminhos e expressões por delimitador/linha. Aceitar é uma edição normal, sem executar; digitar, mover cursor ou mudar contexto invalida a requisição por versão/cancelamento próprio. ONNX continua isolado em Infrastructure e reutiliza sessão CPU. A pasta externa preparada é descoberta quando não há caminho salvo; não se distribuem pesos. [Guia e limites](21-autocomplete-local.md).


## ADR-031 — Highlighting semântico central sobre TextPresenter (12/09/2026)

Manter TextBox como proprietário de texto, undo, foco e seleção; estender apenas seu TextPresenter por template opt-in. Lexer tolerante na Application, vocabulário MongoDB central, snapshots/cancelamento por presenter, cache incremental por linha e processamento em worker. Desktop resolve tokens por recursos Syntax.* dos temas existentes e limita spans ao viewport para documentos extensos. Metadados vêm dos perfis/nós já conhecidos, sem I/O na digitação. Não adicionar AvaloniaEdit, parser completo ou dependência comercial. O TextBox ainda mede todo o texto; a entrega não promete virtualização integral, folding textual nem latência constante para arquivos ilimitados. [Detalhes](22-syntax-highlighting.md).

## ADR-032 — Modo de identificador acima da representação UUID (12/09/2026)

Revisa a apresentação da ADR-028 sem mudar seu codec. A preferência “Representação UUID padrão” misturava dois conceitos; passa a existir `IdentifierRepresentationMode` (`Standard`, `ObjectId`, `UuidV4`), global e persistido em `WorkspacePreferences.IdentifierMode`, enquanto `UuidRepresentation` segue separada e sobrescrevível por conexão. `Standard` significa ObjectId + UUID com o tipo BSON original; o nome `StandardUuid` foi evitado. Migração aditiva: ausência do campo resulta em Standard, sem derivar nada de `UuidRepresentation`.

`IdentifierRepresentationService` no Core é o único ponto de detecção, parsing, formatação, geração, placeholders de script e reescrita de construtores; UI e adaptadores apenas o chamam. Valor BSON prevalece sobre heurística textual. O modo altera geração, `_id` de exemplo e a exibição adicional em UUID v4; não restringe entradas explícitas nem reescreve dados.

Não havia conversão ObjectId → UUID no código. Foi definida uma representação alternativa determinística e reversível (12 bytes + 4 bytes zero). Alternativas descartadas: UUID v5/v8 por hash (irreversível e com aparência de identificador novo) e preenchimento à esquerda (perde a ordenação temporal do ObjectId). A alternativa aparece somente onde não é reinterpretada: árvore, identidade, prévia, **Interpretar** e cópia explícita. Texto reinterpretável (JSON exibido, editor, cópias JSON e consulta por `_id`) mantém `ObjectId("…")`, evitando que uma preferência visual grave Binary no lugar de ObjectId. O JSON humano passou a mostrar `ObjectId("…")` em todos os modos, coerente com a árvore; o `$oid` canônico continua fonte de identidade, precondição e exportação. [Regras](06-editor-bson-e-uuid.md#modo-de-identificador--implementado-em-12092026).

## ADR-033 — Contratos ONNX por arquitetura e inferência compartilhada (13/09/2026)

Estende ADR-027: Qwen FIM nativo e DeepSeek FIM gerenciado em .NET, escolhidos pelo catálogo/manifesto. Não inferir suporte a qualquer Llama só pelo nome do decoder. Prompt, markers e decode são verificados contra vetores externos independentes. A lista original de comandos do treino é preservada no cabeçalho DeepSeek. Input vazio não gera cabeçalho adicional.

WinML 0.15.2 é a distribuição Windows padrão; CPU permanece padrão Linux e alternativa explícita. CUDA é build separado com lockfile próprio. Não misturar bibliotecas nativas de backends. CPU usa seleção de núcleos do ORT. Fallback GPU também cobre falha na geração, mantendo status efetivo e sessão CPU reutilizada.

IA do chat passa pela mesma fila/sessão do autocomplete, com contexto integral obrigatório, cancelamento por chamada e propostas sem execução. O pacote FIM não é anunciado como modelo conversacional: o teste real mostrou alteração adicional não solicitada. Preservar confirmação e diff; recusar saída truncada. [Especificação e evidências](23-onnx-slopcoder.md).


## ADR-034 — Datas BSON em construtor UTC (13/09/2026)

Apresentação humana usa `ISODate("yyyy-MM-ddTHH:mm:ss.fffZ")`; o `Z` torna explícito que o instante está em UTC. BSON armazena apenas o instante UTC em milissegundos e não conserva o fuso original. BsonDatePresentation centraliza parsing e apresentação, integrado ao fluxo existente de construtores. Entradas aceitam Date/ISODate com texto, incluindo ISO 8601 com offset; normalizam para milissegundos BSON. Strings e datas fora do intervalo representável não são convertidas na apresentação. Console e helper de mongosh fazem Date(texto) produzir data nativa; chamadas sem argumentos e métodos estáticos preservam comportamento JavaScript. Exportações mantêm Extended JSON. Não há mudança de persistência nem conversão automática de campos string.

## ADR-035 — Roadmap por versão e status baseado em evidência (13/09/2026)

**Estado: aceita para a documentação.** O plano anterior F0–F7 misturava abrangência futura com recursos já implementados e vinculava scripts/administração ao MVP. Ele é substituído por seis fases: v0.5.0 MVP; v0.6.0 consultas avançadas; v0.7.0 manutenção; v0.8.0 automação JavaScript entre conexões; v0.9.0 inteligência local; v1.0.0 estabilidade.

Cada fase registra objetivo, inclusões, exclusões, aceite, dependências, status e documentos. IDs existentes são preservados; EDT-08 explicita formatação. O inventário distingue recorte implementado de requisito completo e evidência estática de homologação real. CSV e formatador geral de query/script bloqueiam o aceite da v0.5.0. Recursos avançados existentes continuam antecipações disponíveis; não serão removidos, reimplementados nem declarados estáveis por remapeamento documental.

A última tag alpha encontrada localmente é v0.1.1-alpha; isso não comprova publicação remota nem atribui v0.5.0 ao checkout. O pipeline continua versionando por tag. Não há mudança de runtime, UI ou persistência nesta decisão. Console Jint e Script/mongosh mantêm seus contratos distintos, assim como os invariantes de contexto, cancelamento, BSON, privacidade e confirmação.

Referências comparativas a ferramentas proprietárias são retiradas da documentação. Preservar MIT e avisos das dependências reais; não inferir garantia jurídica de gratuidade ou código aberto. Documentação técnica necessária a MongoDB, protocolos e capacidades não é recomendação comercial. [Roadmap](09-plano-de-implementacao.md) e [inventário](24-inventario-roadmap.md) são as referências vigentes de entrega.

## ADR-036 — Operações concorrentes e polimento do MVP (13/09/2026)

**Estado: implementada; homologação multiplataforma pendente.** IApplicationOperationService/ApplicationOperationService mantém snapshots imutáveis de operações ativas, prioridade, início, estado e progresso. Cada escopo possui CTS ligado somente ao token do chamador. Cancelar solicita o token e conserva a operação visível até o término. A UI observa via seu SynchronizationContext; não inicializa um dispatcher em viewmodels sem aplicação. Só a última conclusão é retida e as mensagens da barra expiram em seis segundos. Reportes são limitados a dez por segundo, com último percentual real imediato. Operações manuais não são ocultadas por autocomplete.

A exportação MVP permanece a página carregada: união de campos CSV em duas passagens limitadas à página, uma linha/documento por escrita, UTF-8, proteção de fórmulas por apóstrofo e valores complexos canônicos. Um arquivo `.partial` exclusivo é publicado por move após sucesso; falha/cancelamento remove o parcial e não sobrescreve destino existente. LocalResultPageExportService encapsula I/O. O picker usa o tipo selecionado quando o SO o informa, com extensão como fallback.

MongoCodeFormatter usa o parser Acornima já transitivo de Jint. Só insere indentação em limites sintáticos; não executa nem regenera literais. Limites de tamanho/profundidade, worker e token; resposta obsoleta é descartada e aplicação ao TextDocument conserva undo. A apresentação de resultados também é preparada em worker; mudança de representação de resultado grande usa geração cancelável. O reparo de linhas gigantes usa transformação visual porque a implementação do AvaloniaEdit pula generators para linhas extensas; seleção/offsets/undo continuam ligados ao documento original.

MongoClientPool em DI reutiliza clientes por settings efetivos, inclusive timeout/configuração. Mantém no máximo 64 configurações por sessão, recusa excedente com mensagem e libera ao encerrar; não descarta cliente em uso. Mudanças de configuração geram nova chave; não há eviction de pool ativo. É um limite conservador, não um gerenciador de reconexão completo. O proprietário LiteDB conserva o JSON do ambiente em memória e devolve cópias independentes, publicando novo estado só após salvar. Nenhuma segunda conexão LiteDB ou mudança de formato da sessão foi introduzida.

Find usa paginação no servidor. Páginas driver são limitadas a 8 milhões de caracteres canônicos; erro solicita projeção/limite menor. Skip alto mantém custo de servidor: paginação por chave exige ordenação estável e contrato próprio futuro, sem alterar silenciosamente a semântica de skip do usuário. Metadados/autocomplete usam fontes conhecidas; IA continua opcional fora do aceite do MVP.

## ADR-037 — Catálogo multimodelo e serviço central de IA local (13/09/2026)

Revisa ADR-027, ADR-030 e ADR-033. O modelo deixa de ser um caminho configurado e passa a ser um recurso substituível: catálogo por subpastas de um diretório → modelo selecionado → capacidades → hardware/provider → ONNX Runtime → autocomplete/chat.

`ILocalAiModelService` (Application) é o único dono do modelo carregado: descoberta, validação, carga sob demanda, troca, descarga, fila com prioridade (ação explícita antes do autocomplete), cancelamento, capacidades e teste completo. Autocomplete e chat não conhecem backend. A carga não usa o token do editor, para que digitar não aborte o carregamento; um modelo por vez, liberado antes do próximo. Diferenças de arquitetura ficam em `IModelAdapter` na Infrastructure. `slopstudio-model.json` é opcional; a identidade persistida é o nome da pasta. A carga é publicada na barra de operações da ADR-036.

Hardware vem do próprio ONNX Runtime (`GetEpDevices`), não de detecção paralela do sistema operacional, para que a UI só ofereça o que o runtime deste build executa. Automático segue NPU → GPU → CPU entre dispositivos disponíveis e compatíveis, com fallback. Escolha explícita não faz fallback silencioso: substitui a regra anterior de "GPU mantém CPU como fallback", que ocultava a incompatibilidade.

Preferências continuam aditivas em `AutocompleteSettings` versão 1 (`ModelDirectory`, `SelectedModel`, `ChatModel`, `ChatEnabled`); a estrutura `Ai` conceitual não virou documento novo para preservar migração e recuperação. Removidos os caminhos de máquina fixos e a sugestão automática: configuração específica de máquina não pertence ao código. `ModelPath` salvo continua como pasta externa. Descartados: carregar o modelo no startup, manter vários modelos em memória, `FileSystemWatcher` no MVP e medição de VRAM sem API do runtime. [Especificação e limites](26-ia-local-multimodelo.md).
