# Decisões propostas

Estado: **Plano revisado** em 15/09/2026. AC-03/04/05/06/11/14 têm base implementada na Fase 1, com aceite parcial e divergências registradas ([estado e desvios](phases/phase-1-data-traditional.md#estado-da-implementação)). AC-08 (política de atalhos, lote W0) tem base implementada e validada em 18/09/2026, com o detalhe de estado registrado na própria decisão abaixo. Ao serem aceitas, promover para [10 — ADRs](../10-decisoes-arquiteturais.md) com numeração oficial e revisão explícita das ADRs afetadas; essa promoção ainda não foi feita.

## AC-01 — Contratos in-process inspirados em LSP

**Contexto.** A IDE é um processo .NET único; LSP define conceitos maduros de completion e inline completion.
**Decisão.** Contratos internos espelham LSP (gatilho, lista incompleta, faixa insert/replace, resolve tardio, inline separado), sem servidor LSP.
**Alternativas.** Servidor LSP em processo separado (rejeitado: serialização, sincronização de documento e ciclo de vida sem benefício atual).
**Consequências.** Exposição futura via LSP continua possível por adaptador.

## AC-02 — Parser tolerante próprio sobre lexer compartilhado

**Contexto.** O cursor quase sempre está em código incompleto; Acornima lança erro nesse caso; tree-sitter exige binários nativos por RID.
**Decisão.** Extrair o lexer do highlighting e construir parser tolerante para o subconjunto necessário, com reparse por statement.
**Alternativas.** Acornima (sem tolerância), tree-sitter (portabilidade/publicação ARM64), regex por caso (rejeitada pela meta e pelo P-01).
**Consequências.** Manutenção de gramática própria limitada; testes de propriedade e diferencial obrigatórios; Acornima continua na validação/formatação.

## AC-03 — Linguagem MongoDB como dados versionados

**Contexto.** Vocabulário duplicado em sete lugares; regras de contexto tenderiam a `if`/`switch`.
**Decisão.** Arquivo embutido com símbolos, assinaturas, shapes e snippets; fonte única para autocomplete e projeção do highlighting; teste de contrato com o Console.
**Alternativas.** Listas C# por serviço (atual); gerar a partir da documentação oficial em tempo de execução (rejeitado: rede e instabilidade).
**Consequências.** Adicionar comando é editar dados + caso de teste; revisão do arquivo a cada versão de servidor suportada.

## AC-04 — Sem novo projeto de domínio

**Contexto.** `AGENTS.md` e [05](../05-arquitetura.md) desencorajam projetos paralelos até haver contrato estável.
**Decisão.** Namespaces `Application.Language.*` sem pacotes, protegidos por teste de arquitetura. Benchmarks em projeto de ferramenta separado.
**Alternativas.** Projeto `EsilvaSoft.SlopStudio.Language` imediato (adiado até estabilizar a Fase 2).
**Consequências.** Extração futura mecânica se necessária.

## AC-05 — Política de acesso remoto do autocomplete

**Contexto.** [21](../21-autocomplete-local.md) afirma que o autocomplete nunca executa consultas; o Explorer nunca consulta automaticamente ao abrir coleção; campos úteis exigem schema.
**Decisão.**
- Comandos de **metadados** (`listDatabases`/`listCollections` nameOnly, `listCollections` com options por banco, `listIndexes`) podem ser carregados sob demanda **somente para o perfil já conectado da aba**, deduplicados, com TTL, prioridade baixa e cancelamento.
- **Amostragem de documentos** só por ação explícita ("Amostrar schema") ou opt-in por conexão, com pipeline que devolve apenas nomes e tipos.
- Valores de documentos nunca são armazenados no catálogo nem enviados ao modelo.
**Alternativas.** Proibir qualquer chamada (catálogo ficaria limitado aos nós expandidos); amostrar automaticamente (viola invariantes).
**Consequências.** Revisa a redação de [21](../21-autocomplete-local.md) para distinguir metadados de consultas a dados.

## AC-06 — Tabelas ordenadas por escopo

**Decisão.** Arrays ordenados com busca binária para prefixo e índice de camel humps; substring limitada ao escopo.
**Alternativas.** Trie/FST (sem ganho para escopos ≤ 10⁴ reconstruídos por inteiro), varredura linear (custo por tecla).
**Consequências.** Validação pelo benchmark da Fase 1; troca por outra estrutura não altera contratos.

## AC-07 — `CompletionWindow` e snippets do AvaloniaEdit

**Decisão.** Substituir `MenuFlyout` por `CompletionWindow` com ranking próprio e snippets nativos (`SnippetInputHandler`).
**Alternativas.** Controle próprio de lista (mais código e acessibilidade a refazer); manter `MenuFlyout` (sem filtro, navegação ou placeholders).
**Consequências.** Estilização e verificação de virtualização no Headless; atualização do design system.

## AC-08 — Atalhos

**Estado: implementado (W0, 18/09/2026).** Build 0 avisos; suíte 1170 aprovados, 0 falhas.

**Decisão revisada.** `Ctrl+.` foi removido dos padrões por decisão explícita do produto; `Ctrl+Espaço` é o único gatilho padrão da lista básica, `Ctrl+;` pede IA (reconhecido, mas sem runtime nesta entrega — nunca insere `;`, nunca abre a lista no lugar). Doze comandos (`EditorCommandIds`) organizados em quatro escopos (`EditorCommandScope { Global, List, Snippet, Inline }`), cada um resolvido isoladamente por `EditorCommandDispatcher.Match`; o mesmo gesto pode ser padrão em escopos diferentes (`Tab`/`Esc` servem lista, snippet e ghost), mas duplicidade dentro do mesmo escopo torna a sessão ilegível. Registro persistido de forma aditiva em `EditorKeyBindings` (`CurrentVersion = 1`, sem migração de dado), sem tela de edição nesta meta. Como os padrões nunca são gravados na sessão, um override anterior de `Ctrl+.` continua legível e funcional, sem ser removido, adicionado ou reescrito. Pontuação casa pelo símbolo produzido pelo layout, com o símbolo físico como alternativa; teclas nomeadas casam por identidade de tecla — corrigido um defeito em que a decisão anterior (`||` entre as duas comparações) deixava `Ctrl` sozinho abrir a lista e uma tecla desconhecida aceitar o ghost, por `null == null` casar com qualquer gesto quando o evento não carregava tecla nem símbolo. Ver [editor-integration.md](editor-integration.md#arbitragem-de-teclado).
**Limitação conhecida, não resolvida.** Sem `Ctrl+.`, `Ctrl+Espaço` é o único disparo padrão do básico e colide com a troca de IME em Windows e Linux. Esta meta não entrega tela de edição de atalhos; o único contorno é um override manual em `EditorKeyBindings`.
**Alternativas.** Manter `Ctrl+.` como principal (rejeitado nesta revisão pelo produto); casar só por `Key` (quebra em layouts como ABNT2).
**Consequências.** Homologação nativa de layouts, plataformas e IME reais permanece pendente (ABNT2/US em Windows; X11/Wayland em Linux); teste Headless não substitui essa homologação.

## AC-09 — Caminhos com ponto sempre entre aspas

**Contexto.** A meta usa `{ Cliente.Id: … }`, que é sintaxe inválida em JavaScript.
**Decisão.** O parser recupera o padrão como caminho de campo e a sugestão insere `"Cliente.Id"`, substituindo o trecho digitado.
**Consequências.** Exemplos da documentação e snippets usam aspas em caminhos.

## AC-10 — Contratos de contexto de IA versionados por modelo

**Contexto.** Pacotes SlopCoder foram treinados com o cabeçalho atual de `AutocompleteContextBuilder`.
**Decisão.** `contextContract` opcional no metadata do modelo; ausência = `editor-context-v1`, congelado com teste byte a byte. Novos formatos só após avaliação.
**Consequências.** Formatos novos para modelos base; modelos ajustados podem exigir novo treino para migrar.

## AC-11 — Métricas locais com `System.Diagnostics.Metrics`

**Decisão.** `Meter`/`ActivitySource` com lista fechada de tags, sem texto nem nomes de objetos do usuário; coleta em memória, sem upload.
**Alternativas.** Estender apenas `Trace` (não agregável); telemetria remota (fora da política de privacidade).

## AC-12 — Dois preemptivos e híbrido sequencial

**Decisão revista.** TraditionalPreemptiveCompletionProvider e AiPreemptiveCompletionProvider independentes, com contexto/catálogo e presenter comuns. Tradicional forte publica e encerra; IA só sem candidato forte, LoadedOnly/Background após debounce. Não trocar ghost visível no padrão; extensão concorrente fica experimental. Movimento sem edição não dispara; typeahead só de candidato concluído. Flags/defaults em configuration.md.

## AC-13 — Ghost text no layout do AvaloniaEdit

**Decisão.** Elemento visual gerado na posição do cursor e objeto inline para linhas seguintes, substituindo a sobreposição que redesenha o sufixo.
**Consequências.** Risco de caret/hit-testing multilinha a validar; alternativa em camada de fundo documentada.

## AC-14 — Orçamentos provisórios e baseline

**Decisão.** Números em [performance.md](performance.md) são provisórios; a Fase 1 mede o código atual e as fases revisam os orçamentos com dados antes do aceite.

## AC-15 — Prefix cache como experimento

**Decisão.** Reuso de KV com `Generator.RewindTo`, desligado por padrão; habilitado por provider somente com teste de equivalência greedy aprovado e ganho de TTFT medido.

## AC-16 — Uso recente em memória

**Decisão.** Estatística de aceite apenas na sessão; persistência futura por opt-in, somente nomes, respeitando opt-outs de histórico.

## AC-17 — Lista tradicional não abre automaticamente por padrão

**Decisão.** Opção `CompletionAutoOpenOnTrigger` (`.`/`$`) desligada até haver métricas de aceite e ruído; evita competir com o ghost.

## AC-18 — `Ctrl+;` como prévia inline

**Decisão.** A IA explícita apresenta uma prévia inline rica com indicador e streaming; se indisponível, abre a lista tradicional com o motivo.
**Alternativas.** Lista de sugestões de IA (modelos pequenos greedy geram um candidato; lista com um item é pior que prévia).

## Divergências em relação à meta

| Meta | Decisão | Justificativa |
| --- | --- | --- |
| `{ Cliente.Id: … }` | Caminho entre aspas | JavaScript inválido (AC-09) |
| Criar `IAutocompleteModel`, `IModelTokenizer`, `IInferenceRuntime`, `IModelContextBuilder` | Manter tipos existentes e mapear nomes | Abstrações equivalentes já existem ([architecture.md](architecture.md#mapeamento-de-nomes-da-meta)) |
| Atalhos configuráveis | Registro mínimo sem tela | Não havia sistema; escopo controlado (AC-08) |
| Formato YAML de contexto | Um de cinco formatos avaliados | Decisão empírica e compatível com modelos treinados (AC-10) |
| Fase 2 inclui parser | Mantido, com extração do lexer | Evita segundo lexer (AC-02) |
| Catálogo consulta metadados | Com restrição de conexão conectada e sem amostragem automática | Invariantes do produto (AC-05) |
| Estrutura de catálogo de exemplo | Símbolos + escopos + shapes + evidências | Representa contexto válido, não só nomes (AC-03) |
| `Enter` aceita conforme editor | Configurável, padrão aceitar | Comportamento do `CompletionWindow` |
| Tradicional só explícito | Revisto: tradicional explícito e preemptivo contextual independentes | AC-12; AC-17 trata apenas popup automático |

## Relação com ADRs existentes

| ADR | Efeito se aceitas |
| --- | --- |
| ADR-007 | Mantida: determinístico local como base; ampliada com catálogo e shapes |
| ADR-023 | Mantida: editor-first; snippets e diagnóstico contextual detalhados |
| ADR-027 / ADR-030 | Revisadas: dicionário e contexto de IA passam a vir do Context Engine e de contratos versionados |
| ADR-031 | Revisada: lexer passa a ser compartilhado com o parser; descrições de TextBox já são históricas |
| ADR-033 / ADR-037 | Mantidas: serviço central e adapters; extensões de runtime e metadata |
| ADR-036 | Mantida: operações de metadados usam a barra com prioridade baixa |


## Decisões adicionais da revisão

### AC-19 — Reconciliar base implementada antes de expandir

Consolidar gerações do cache, identidade de ambiente, Peek, cobertura/truncamento e limites de mescla. Não reimplementar catálogo/benchmarks nem exigir lock-free. Fase 1 permanece aceite parcial até evidência pendente; K11–K17.

### AC-20 — Quatro providers, dois pipelines

ICompletionProvider comum; TraditionalCompletionProvider/TraditionalPreemptiveCompletionProvider usam CompletionService. AiCompletionProvider/AiPreemptiveCompletionProvider usam AiGenerationPipeline. Shared stamp/context/schema/caches/ranking/snippets/metrics/presenter; nenhuma árvore própria por provider. Score IA não comparável ao tradicional.

### AC-21 — LoadedOnly atômico e cache tokenizado correto

Política de carga sob gate com revisão de modelo; automático não carrega/troca nem faz fallback que inicialize sessão. Tokenizer pertence ao runtime; blocos BPE só concatenados com fronteiras provadas. Prefix cache opcional medido, não prerequisite de IA explícita.

### AC-22 — Entrega incremental sem ciclo de UI

Presenter prototipado no 2; 5.1 pode seguir a 2 paralelamente a 3. 5.2 após 4 e coordinator; 5.3 integra. Remover legado só após último chamador/paridade. Adiar histórico de valores, Backspace restaurador, atalhos extras, green tree completa e cinco contratos IA de produção sem medição.

### AC-23 — Preferências independentes com migração explícita

Usar campos v1 existentes para modelo/provider/orçamento; flags aditivas separadas para os dois automáticos. Novo usuário: IA inline false; v1 legado preserva intenção por presença do campo, sujeito a LoadedOnly. Matriz de precedência em configuration.md; não sobrescrever sessão ilegível.

Estas decisões atualizam o plano; não marcam UI ou providers implementados. Relação documental com ADR-007/027/030/031/033/037 registrada em docs/10; evidências futuras exigidas por tarefa.


## AC-24 — Aprendizado contínuo de find e persistência LiteDB

Novo requisito de 15/09/2026 substitui adiamento da persistência de schema. Reutilizar amostra dos resultados já retornados, enfileirar sem bloquear consulta/UI, extrair deltas probabilísticos e persistir estrutura/estatísticas no proprietário LiteDB existente. Identidade segura de origem, transação/idempotência, projeção/cobertura, opt-outs e limites definidos em schema-learning.md. Campos aprendidos são observados, não schema rígido nem prova de ausência. Nenhuma consulta adicional automática. Tarefas L11–L16, dono MongoDB Knowledge, com testes/performance desde o hook.

## Pendências arquiteturais registradas

**Revisão de código de 18/09/2026.**

Estas quatro entradas não são decisões fechadas de escopo novo: são pendências identificadas na revisão de código de K11/K14/K16/L, registradas com seu próprio gatilho de reabertura para não serem perdidas nem confundidas com defeito atual.

### PEND-K11-SECRET

**`ConnectionIdentity` não muda; revisão de segredo é tratada por invalidação.** `ConnectionIdentity` permanece derivada da configuração bruta (`ProfileId` + `TargetHost` + fingerprint da connection string salva). Revisão de ENV/cofre é tratada por **invalidação**, não por identidade: salvar ambientes dispara `InvalidateEnvironment` → `MetadataCache.Disconnect(profileId)`.

**Gatilho de reabertura.** Surgir um chamador real de `IConnectionSecretStore.SetPassword`/`Remove` capaz de trocar credencial **sem** desconectar o perfil; nesse dia o chamador passa a ser obrigado a publicar `new MetadataInvalidation(profileId, MetadataChange.Connection)` no `IMetadataInvalidationBus`, e ainda assim **não** é preciso alterar `ConnectionIdentity`. Vira defeito real somente se alguém servir metadado de um cache não invalidado após troca de credencial.

### PEND-K11-L

**`ConnectionIdentity` não pode virar chave persistida de schema aprendido.** A fase L não pode usá-la para esse fim. L14 define chave própria e estável (`ProfileId` + `Database` + `Collection` + versão de schema), sem URI, credencial, host resolvido ou revisão volátil. A identidade governa apenas escopo em memória.

### PEND-K14-KIND

**`CollectionKind` fica `Unknown` até a definição carregar; perda aceita da API escolhida.** A listagem de coleções devolve `CollectionKind.Unknown` até a definição ser carregada. `MongoDB.Driver 3.11.1` não expõe `nameOnly`/`authorizedCollections` em `ListCollectionsOptions`, e as operações de baixo nível são internas; obter o tipo no mesmo custo exigiria `RunCommandAsync("listCollections", nameOnly: true, authorizedCollections: true)` **com drenagem manual de cursor (`getMore`/`killCursors`)**, sob pena de truncar a listagem. Perda aceita como consequência consciente da API escolhida; `WithKnownKinds` retro-preenche o tipo quando a definição chega.

**Gatilho de reabertura.** Qualquer regra de **comportamento** (não de ícone) passar a depender do tipo antes da definição — por exemplo suprimir sugestões de escrita em view/time series, ou a fase L pular views no aprendizado. Implementação então restrita a `MongoMetadataSource.cs`, com teste contra MongoDB real em banco com mais coleções que um lote.

### PEND-K16-QUOTA

**`KnowledgeCatalog.Query` sem cota por tipo/fonte; pré-requisito de L15.** `KnowledgeCatalog.Query` drena as fontes em ordem até `MaximumCandidates`, sem cota por tipo ou por fonte. Sem efeito enquanto as fontes atuais não disputam os mesmos kinds.

**Gatilho.** É **pré-requisito de entrada** de `LearnedSchemaCatalogSource` (L15) — quando duas fontes passarem a produzir `Field`/`Collection` para o mesmo alvo, a ausência de cota deixa uma fonte ocultar a outra, e o truncamento deixa de ser reportável com honestidade. Implementar como **K16-b antes de L15**, não depois (ver [execution-plan.md](execution-plan.md)).

## Correções da revisão W1-K/W2a/W3 (18/09/2026)

### DEC-INLINE-LOADEDONLY

**A política de carga é parâmetro de `GenerateAsync`, não filtro do chamador.** `ILocalAiModelService.GenerateAsync` recebe `AiModelLoadPolicy`. Com `LoadedOnly`, o serviço atende **somente** se o modelo da chave exata (pasta + aceleração + execution provider) já estiver carregado; caso contrário lança `LocalModelUnavailableException` com `UnavailableReason` tipado, sem passar por `EnsureLoadedAsync` — portanto sem carregar, descarregar ou trocar o modelo de outra funcionalidade. O caminho automático (ghost) usa `LoadedOnly`; o pedido explícito continua podendo carregar sob demanda. Filtrar por `LocalModelStatus.State` no chamador é insuficiente: `Status` reflete *algum* modelo pronto, não o desta chave.

### DEC-INLINE-SOURCES

**`CompletionSourcePolicy` transporta a decisão de `InlineCompletionPolicy` até o serviço.** As origens permitidas (dicionário, IA) e a permissão de carga são decididas uma única vez pela política do ghost e viajam como valor por `CompletionSession` → `IAutocompleteService` → `AiAutocompleteProvider`. Nenhum consumidor recombina flags e nenhuma origem desligada reaparece por atalho interno (era o caso do dicionário em `GetImmediateCompletion` com `InlineUseTraditional = false`).

### DEC-INLINE-INCOMPLETE

**`CompletionList.IsIncomplete` cobre as três formas de corte.** Além de `CatalogCompleteness != Complete`, também o esgotamento de `MaximumCandidates` (que uma fonte única não reporta como `Partial`) e o corte por `MaximumItems` com mais candidatos do que os devolvidos. O portão de confiança do ghost depende de "o conjunto inteiro foi visto" para afirmar continuação única; sem os dois últimos sinais ele afirmaria unicidade a partir de uma amostra.

### DEC-INLINE-SNAPSHOT

**O caminho automático recebe o `ITextSnapshot` do editor.** `WorkspaceTabViewModel.GetInlineCompletionAsync` tem sobrecarga por snapshot, alimentada com o `AvaloniaTextSnapshot` do documento. Só assim `TokenCache` reaproveita a lexificação da tecla anterior — um `StringTextSnapshot` novo por tecla nunca casa com a entrada anterior. A sobrecarga por texto mantém contador e identidade de documento **próprios** do automático, separados dos da lista explícita.
