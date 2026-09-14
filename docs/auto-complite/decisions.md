# Decisões propostas

Estado de todas: **Proposta** (14/09/2026). Ao serem aceitas, promover para [10 — ADRs](../10-decisoes-arquiteturais.md) com numeração oficial e revisão explícita das ADRs afetadas.

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

**Decisão.** `Ctrl+.` abre a lista (principal), `Ctrl+Espaço` permanece como alias, `Ctrl+;` pede IA. Registro mínimo de comandos persistido de forma aditiva, sem tela de edição nesta meta. Pontuação casa por `KeySymbol`, com `PhysicalKey` como alternativa.
**Alternativas.** Manter só `Ctrl+Espaço` (conflita com troca de IME em alguns sistemas); casar por `Key` (quebra em layouts como ABNT2).
**Consequências.** Homologação nativa de layouts e plataformas.

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

## AC-12 — Preemptivo em camadas

**Decisão.** Camada 0 determinística sem debounce; camada 1 IA com debounce adaptativo, só com modelo já carregado e latência aceitável; typeahead sobre o ghost; movimento de cursor sem edição não dispara.
**Alternativas.** Somente IA (lento em CPU e dependente de modelo); inferência por tecla (rejeitada pela meta).

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
| Tradicional só explícito | Mantido, com opção futura de abertura automática | AC-17 |

## Relação com ADRs existentes

| ADR | Efeito se aceitas |
| --- | --- |
| ADR-007 | Mantida: determinístico local como base; ampliada com catálogo e shapes |
| ADR-023 | Mantida: editor-first; snippets e diagnóstico contextual detalhados |
| ADR-027 / ADR-030 | Revisadas: dicionário e contexto de IA passam a vir do Context Engine e de contratos versionados |
| ADR-031 | Revisada: lexer passa a ser compartilhado com o parser; descrições de TextBox já são históricas |
| ADR-033 / ADR-037 | Mantidas: serviço central e adapters; extensões de runtime e metadata |
| ADR-036 | Mantida: operações de metadados usam a barra com prioridade baixa |
