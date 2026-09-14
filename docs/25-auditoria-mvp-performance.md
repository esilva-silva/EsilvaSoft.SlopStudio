# Auditoria do MVP e polimento de performance

Inventário inicial em 13/09/2026, antes de alterar a implementação. Escopo oficial: Fase 1 / v0.5.0 do [roadmap](09-plano-de-implementacao.md), interpretado pelo [inventário](24-inventario-roadmap.md) e pelos ADRs. A solicitação acrescenta acompanhamento global concorrente e revisão de responsividade. Implementado significa caminho concreto no checkout, não homologação real.

| Requisito obrigatório / recorte | Classificação inicial | Evidência e pendência |
| --- | --- | --- |
| Cadastro, edição, exclusão e abertura de perfis | ✅ Implementado | ConnectionsViewModel, WorkspaceService e proprietário LiteDB; verificar erros e responsividade |
| URI/opções/autenticação delegadas ao driver | 🚧 Parcialmente implementado | MongoWorkspaceService e ConsoleDatabaseSession; matriz real TLS/SRV/autenticação continua separada |
| Explorer Connection → Database → Collection → Documents | ✅ Implementado | WorkspaceViewModel/ExplorerNodeViewModel; carga sob demanda e abertura sem executar |
| find/findOne, filtro, sort, limit e skip | ✅ Implementado | ConsoleDatabaseSession e MongoWorkspaceService; limite no servidor, cancelamento; medir e revisar paginação |
| Visualização JSON/árvore e seleção por origem | ✅ Implementado | StructuredResults e WorkspaceTabViewModel; revisar custo de apresentação na UI |
| Insert e duplicação | ✅ Implementado | MongoWorkspaceService e DocumentMutationViewModel; validação/confirmacão existentes |
| Update/replace e edição BSON | ✅ Implementado | DocumentMutationViewModel; identidade, conflito e projeção protegidos; homologação real pendente |
| Delete/deleteMany | 🚧 Parcialmente implementado | Confirmação existe; revisar inclusão de filtro no Console e contexto capturado |
| BSON/UUID/ObjectId/datas | ✅ Implementado | Codecs e fixtures independentes existentes; preservar os contratos |
| Autocomplete simples sem IA | ✅ Implementado | MqlAutocompleteService/BasicAutocompleteProvider; revisar cancelamento, debounce e fontes limitadas |
| Formatar JSON/query/script pelo editor, com undo | ❌ Não implementado | ExtendedJsonFormatter apenas apresenta resultados; falta comando integrado |
| Exportar página JSON | ⚠️ Implementado com problema | QueryResultExportSerializer materializa buffer, array e string completos; revisar escrita incremental e cancelamento |
| Exportar página CSV | ❌ Não implementado | Implementar contrato TRF-01: união de campos, BSON canônico, escaping e política de fórmulas explícita |
| Abas, contexto e cancelamento isolados | ✅ Implementado | WorkspaceTabViewModel captura entradas; revisar operações concorrentes e retornos obsoletos |
| Histórico, arquivos, rascunhos e recuperação | ✅ Implementado | WorkspaceService/LiteDbConnectionProfileRepository; limites, opt-out, Input opt-in e falhas visíveis |
| Temas e teclado | ✅ Implementado | Avalonia/WorkspaceUiTests; renderizar alterações em ambos os temas e tamanhos/escalas exigidos |
| Auditoria local e privacidade | ✅ Implementado | Retenção de 500 metadados; revisar erros e ausência de segredos |
| Barra inferior global com prioridades, progresso e cancelamento | ❌ Não implementado | Status local existente; falta coleção central de operações simultâneas e estado terminal temporário |
| Reutilização de MongoClient | ⚠️ Implementado com problema | MongoWorkspaceService.CreateClient cria a cada operação; ConsoleDatabaseSession reutiliza apenas dentro de uma execução |
| Startup, memória e operações assíncronas | 🚧 Parcialmente implementado | LiteDB usa worker e Console executa Jint em Task.Run; revisar trechos síncronos antes dos awaits, apresentação, metadados e caches |

Pendências de implementação, em ordem: serviço concorrente central e feedback inferior; formatação segura e cancelável; JSON/CSV incremental da página; reutilização de clientes e correções verificadas de responsividade/contexto; testes de falha/concorrência, medições e inspeção visual; atualização dos documentos de aceite.

Não ampliar o MVP para administração avançada, exportação integral de coleção, importação CSV, IA, automação entre servidores ou cofre nativo. Streaming aqui significa escrever incrementalmente a página já carregada, cujo escopo deve estar explícito. Skip alto continua tendo custo no servidor; não prometer paginação por chave implementada sem evidência.

Homologação Windows/Linux com MongoDB real, diálogos nativos e leitor de tela exige evidência específica; testes Headless não substituem esses gates. Esta lista é a baseline inicial; resultados e pendências finais serão registrados ao concluir a revisão.


## Resultado da implementação e auditoria de aceite — 13/09/2026

O inventário inicial acima foi preservado. As mudanças de implementação foram incorporadas ao checkout (`afec6c8` contém o primeiro conjunto; os ajustes posteriores permanecem no worktree). O MVP **não recebe ainda aceite integral da versão**, porque faltam gates externos declarados abaixo. Não foi criada release, tag ou publicação.

| Solicitação | Estado atual e evidência verificável |
| --- | --- |
| 1. Inventário antes de alterar | ✅ A tabela inicial deste arquivo foi criada antes das alterações; roadmap, catálogo, design system, ADRs e matriz consultados |
| 2. Itens faltantes | ✅ Comando de formatação e JSON/CSV da página integrados; MongoCodeFormatter, QueryResultExportSerializer, LocalResultPageExportService e UI do editor |
| 3. Revisão do existente | ✅ Corrigidos client por operação, geração visual ignorada em linhas longas, serialização completa para exportação, preparação pesada de resultados na UI, salvamento síncrono de ambientes e leitura de ambiente em disco por operação |
| 4. Responsividade | ✅ Serviços Mongo saem da UI após snapshot e antes de parsing/client/driver; arquivo/exportação/formatação/ambientes usam workers; preparo de resultados é assíncrono. Busca estática não encontrou `.Result`, `.Wait()` ou `GetAwaiter().GetResult()` no Desktop. As esperas do host Jint permanecem dentro do worker do runtime |
| 5. Volume de dados | ✅ Find aplica limit/skip/projeção no servidor; páginas driver recusam mais de 8 milhões de caracteres e orientam reduzir limit/projeção; arquivos têm leitura limitada a 16 milhões de caracteres |
| 6. Paginação | ✅ Fixture real de 5.000 documentos verifica duas páginas ordenadas de 100, segundo início no `_id` 100; não busca coleção inteira. Skip alto conserva custo; cursor/chave ainda não foi implementado e exige contrato de ordenação estável |
| 7. Cancelamento | ✅ Token independente em cada escopo/aba, propagado ao driver/worker/arquivo; teste distingue cancelamento de uma operação e permanência das demais; timeout do Console tem estado próprio |
| 8. Autocomplete | ✅ Sugestões explícitas da UI usam snapshot limitado dos namespaces conhecidos, sem I/O adicional; dicionário/operadores/campos/histórico existentes continuam determinísticos e IA opcional. Geração explícita roda em worker com prioridade baixa; debounce/cancelamento existentes preservados |
| 9. Formatação | ✅ JSON conserva tokens; query/script usam a AST, preservam comentários/regex/templates e admitem undo; tamanho/profundidade limitados, execução inexistente, resposta obsoleta descartada. Testes de idempotência, tokens, entrada inválida/cancelada e undo real |
| 10. Exportação | ✅ Página canônica capturada antes do picker; JSON/CSV incremental, CSV com união de campos e proteção de fórmulas, progresso real por documento; testes de escrita antecipada, falha, limpeza, cancelamento e proteção do destino existente |
| 11. Edição | ✅ Fluxo preexistente protegido; comparação de conflito grande vai ao worker. Fixture MongoDB verifica alteração, rejeição de snapshot obsoleto e Int64/data preservados; testes existentes verificam `_id`, projeção, UUID e somente leitura |
| 12. Destrutivas | ✅ Confirmação do Console agora inclui filtro de deleteOne/deleteMany, além de perfil/banco/coleção/método. Confirmação de documento conserva identidade e precondição. Não faz contagem extra silenciosa; estimativa indisponível não é inventada |
| 13. Memória | ✅ Histórico em memória limitado a 50 no caminho de query, namespaces conhecidos limitados, cache antigo de campos liberado, árvores por grupos de 256 com continuação, página e arquivo limitados; resultados seguem fora de snapshots |
| 14–15. Recursos caros/MongoClient | ✅ Pool de DI por settings efetivos; teste garante mesma instância para configuração igual e isolamento para diferente. Limite de 64 configurações por sessão, sem descartar cliente em uso; liberação no encerramento. Reinício exigido se o limite de configurações for atingido |
| 16. Erros | ✅ OperationErrorMessages diferencia JSON/entrada, autenticação, conexão, query/MongoDB, timeout, cancelamento, armazenamento/exportação; mensagens de rede não repetem credenciais; URI redigida em outros erros. Console separa timeout de cancelamento voluntário |
| 17. Logging | ✅ Diagnóstico comum registra categoria, tipo e HResult, sem documento/URI resolvida; logs de autocomplete já usam metadados. Não foi acrescentado logging por documento exportado ou por tecla |
| 18. Startup | 🚧 Medido startup Headless com repositório vazio; versões quentes variaram entre 9 e 464 ms neste host. Teste verifica zero chamadas Mongo durante abertura/formatação. Cold start nativo e perfil de CPU/RAM em ambiente real ainda não medidos |
| 19. Lazy loading | ✅ Explorer conserva carga ao expandir; abrir coleção não executa. Resultados/Documentos carregam campos em grupos; texto completo continua acessível. Não carrega modelo IA ou schema remoto como obrigação do MVP |
| 20. Polimento visual | ✅ Barra inferior com alvos de 28, percentual separado, descrição/dica, operações adicionais e cancelamento; barra de botões quebra linha em janela mínima. PNGs de controles reais em ambos os temas e 18 combinações de tamanho/escala; nenhum golden alterado |

### Evidência executada

- `dotnet restore EsilvaSoft.SlopStudio.slnx --locked-mode`: aprovado; exigiu acesso de leitura à configuração NuGet do usuário pelo mecanismo de escalonamento, sem exibir seu conteúdo.
- `dotnet build EsilvaSoft.SlopStudio.slnx --no-restore -p:UsedAvaloniaProducts=`: aprovado, zero avisos/erros. O parâmetro evita a tarefa externa de telemetria Avalonia, preservando analisadores.
- `dotnet test EsilvaSoft.SlopStudio.slnx --no-build --no-restore --logger 'trx;LogFileName=mvp-polish.trx'`: **549 executados/aprovados, zero falhas**. TRX registra 555 casos descobertos; seis casos de IA explícita não participaram da execução e não fazem parte do aceite MVP.
- `ConsoleMongoIntegrationTests`: **dois testes aprovados** com MongoDB portátil **8.0.30** em processos/portas temporários locais Windows; CRUD/Console entre duas origens e novo cenário de paginação, edição/conflito, BSON e exportação. O novo cenário usou 5.000 documentos, duas páginas de 100 e levou **55 ms** na última execução (84 ms em execução anterior). Não é benchmark de produção nem prova de TLS/replica set/latência WAN.
- `MvpPolishUiTests`: status concorrente, prioridade, progressos determinado/indeterminado, cancelamento independente, troca de aba durante atividade, formatação sem rede e Ctrl+Z usando TextDocument real. PNGs em `tests/EsilvaSoft.SlopStudio.UnitTests/bin/Debug/net10.0/ui-evidence/mvp-status-*.png`. As 18 barras da última execução foram inspecionadas no contato `mvp-status-review.png`, inclusive percentual separado e janela mínima.
- `HugeSingleLineKeepsFullTextAndMakesEveryRangeReachableWithoutShapingItAll`: passou sem reduzir a asserção; linha de 2 milhões de caracteres mantém texto/cópia/undo e janela visual limitada. A falha histórica foi corrigida pela transformação visual.
- `git diff --check`: sem problemas de whitespace. Nenhuma dependência comercial ou nova dependência de pacote foi adicionada.

Os testes de falha de seleção mantêm o mesmo critério de não executar fallback; somente a mensagem esperada recebeu a categoria explícita agora exibida. O teste de salvamento de ambientes passou a aguardar o comando assíncrono; suas verificações de persistência e falha foram mantidas. Não foram alterados golden files.

### Gates ainda abertos — impedem declarar a meta integralmente concluída

1. **Linux com interface gráfica**: não há distribuição listada pelo WSL neste host. Foi solicitado ao usuário acesso a ambiente para homologação; ainda não há resposta. Não executar novamente a suíte Windows para inferir esse gate.
2. **Diálogos nativos/clipboard e navegação real Windows/Linux**: Headless verifica controles e eventos, mas não o seletor de arquivo do SO. O caminho de exportação usa a API de tipo selecionado do Avalonia e precisa de homologação nativa nos formatos anunciados. O plugin Computer Use foi inicializado e listou janelas; a tentativa de abrir a compilação local retornou `Computer Use app approval timed out`. A IDE não apareceu na consulta seguinte de janelas. A homologação nativa depende de abrir/autorizar o aplicativo; isso não é falha de teste da IDE.
3. **Startup e responsividade nativos sob carga**: a evidência cobre workers, limites, token, paginação e renderização sintética, não medição de cold start, movimentação de janela durante I/O WAN ou perfil completo de CPU/RAM. A afirmação absoluta de que a UI nunca trava não pode ser derivada de testes limitados.
4. **Leitor de tela, matriz de autenticação/topologias e mongosh real Linux**: continuam no checklist conforme abrangência do roadmap; a implementação básica e o servidor standalone local não provam esses ambientes.

A infraestrutura pedida está presente e os gaps básicos de código foram fechados. A meta permanece ativa até completar a evidência de aceite aplicável. Recursos de fases futuras continuam fora desta entrega.
