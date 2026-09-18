# Backlog — JavaScript Script Engine entre conexões

**Origem:** antiga Fase 4 / v0.8.0 do roadmap anterior ("Automação JavaScript entre conexões"), IDs EDT-06 e TRF-05. **Situação:** retirado das fases; requisito de planejamento adiado para backlog.

No roadmap oficial atual, a v0.8.0 passou a ser [abertura e salvamento de arquivos de texto](../phases/phase-04-v0.8.0/README.md). A automação entre conexões não tem fase atribuída.

## O que existe hoje e permanece funcionando

Nada foi removido do código. Os contratos já definidos são preservados:

- **Console Jint** (`ConsoleRuntime`, `ConsoleBootstrap.js`, `ConsoleDatabaseSession`) — **continua sendo o modo ativo** do editor. Não é afetado por esta decisão.
- **Modo Script / mongosh** (`MongoshScriptExecutionService`, `MongoshScriptTemplate`, `MongoshOutputParser`) — preservado em Infrastructure, com seus testes.
- Porta `IScriptExecutionService` em Application e a fachada em `WorkspaceService` — preservadas.
- DSL de navegação entre conexões: `getConnection(...).getDatabase(...).getCollection(...)` e `ConnectionPool.*`, reconhecidas pelo highlighting.

## Decisão

O modo **Script** foi desativado no seletor de modos da tela inicial. O `ComboBox` de modos deixou de ser exibido e o único modo oferecido é **Console**.

Mantidos sem alteração: o padrão de edição, contexto, seleção e autocomplete já estabelecido para o editor; o despacho interno por modo em `WorkspaceTabViewModel`, que continua aceitando `"Script"` e `"Agregação"` por código e por rascunho persistido.

O código permanece isolado em seus projetos e pode ser retomado na fase apropriada, sem engine duplicado.

## Limites já documentados a preservar

O exemplo de cópia entre conexões conserva `_id`: duplicados podem falhar e escritas anteriores podem já ter ocorrido. **Não é um sincronizador com retomada.** `ConnectionPull` e `GetDatabase` são exemplos conceituais reconhecidos pelo highlighting, não APIs executáveis — a API real usa `getDatabase` em minúsculo. Não acrescentar aliases sem decisão explícita.

## Escopo futuro, quando houver fase

Navegação programática entre conexões, leitura na origem e escrita no destino com permissões e confirmação por destino, snapshots de perfis/ambientes antes dos awaits, e homologação de cada runtime anunciado em Windows e Linux. Fora de escopo permanente até decisão em contrário: paridade total com mongosh, acesso CLR/Node irrestrito, transação distribuída, rollback ao cancelar e execução agendada com o aplicativo fechado.

## Documentos relacionados

[20 — Console](../20-console.md) · [06 — Editor/BSON](../06-editor-bson-e-uuid.md) · [05 — Arquitetura](../05-arquitetura.md) · [10 — ADRs](../10-decisoes-arquiteturais.md)
