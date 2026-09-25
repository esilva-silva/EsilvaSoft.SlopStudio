# Memória de execução — Fase 7 / v0.11.0

## Preparação dos agentes — 24/09/2026

Perfis centrais ampliados com runtime, MCP, Tool Registry e providers; responsabilidades dos especialistas existentes incorporadas ao [plano 10](../phases/phase-07-v0.11.0/10-plano-de-implementacao.md) e à [matriz 20](../phases/phase-07-v0.11.0/20-agentes-e-execucao.md). [Protocolo comum](../../agents/phase-7-protocol.md) define propriedade de arquivos, handoffs e evidência por gate. [Claude Code](../../agents/claude-code.md) usa `CLAUDE.md` e adapters gerados, sem alterar permissões nem integrar o provider do produto.

Esta atualização é de instruções/planejamento. Não reexecuta testes do produto nem aprova ACs. O registro abaixo preserva o histórico de 23/09; estado de branch, arquivos não commitados, pendências e restrições daquela sessão devem ser conferidos no checkout atual, especialmente a evolução posterior da auditoria descrita no documento 19.

Atualizado em **23/09/2026**. Este arquivo é um ponto de retomada da meta de implementar MCP e integração com agentes externos no EsilvaSoft.SlopStudio. O [plano da fase](../phases/phase-07-v0.11.0/README.md), o [plano de implementação](../phases/phase-07-v0.11.0/10-plano-de-implementacao.md), os [critérios de aceite](../phases/phase-07-v0.11.0/12-criterios-de-aceite.md) e a [matriz de validação](../15-matriz-de-validacao.md) são as fontes detalhadas; este registro resume o estado observado neste checkout.

## Lote 1 — credenciais e identidade — 24/09/2026

A P7-L01 (persistence-security-agent) completou no código, incluindo as correções A2/M5 da revisão independente:

- migração e recuperação versionadas das credenciais Mongo no owner único: journals v1 com recusa de versão desconhecida, limpeza na exclusão, correção de órfão;
- allowlist fechada apenas quando há senha literal; sem senha, só são recusadas opções portadoras de segredo;
- retomada em segundo plano na composição do DI, com `IConnectionProfileCredentialStatusProvider` registrado para a UI;
- `IAgentPrincipalAuthority` como faceta do owner em DI;
- o scan interno `FindCollectionsContainingAsync`.

Resultados no Windows: build oficial 0/0 e filtro focado 313/0/0. Credential Manager nativo aprovado nesta sessão; o resultado difere do `1312` de 23/09. Linux não foi executado. Pendências: contagem na UI, ligação de broker/registry/runtime ao principal, segunda conta, cofre bloqueado e Linux nativo. Gate 1 **parcial**. [Detalhes](../phases/phase-07-v0.11.0/17-validacao-da-meta.md#lote-1--migração-de-credenciais-mongo-identidade-de-canal-e-scan--24092026).

## Meta
Implementar integralmente a Fase 7 — v0.11.0: MCP e integração com agentes externos no EsilvaSoft.SlopStudio, seguindo docs/phases/phase-07-v0.11.0/README.md e seus documentos complementares.
Resultado esperado:
Entregar um Tool Registry único compartilhado pelo servidor MCP e pelo Agent Runtime, chat nativo Avalonia, providers OpenAI/Codex e Claude, integração local independente com ONNX, credenciais seguras e operações MongoDB governadas por permissões, aprovações e auditoria.
Execução:
Usar agents/goal-orchestrator.md para decompor, delegar aos especialistas, acompanhar e validar a meta. Inspecionar o código atual e distinguir funcionalidades existentes de requisitos planejados. Seguir as dependências e os gates dos lotes 0 a 12:
0. Validar contratos, SDKs, protocolos, confinamento, versões e licenças com spikes reproduzíveis.
1. Implementar cofre de credenciais por SO, identidade e migrações recuperáveis.
2. Implementar registry, schemas, autorização, controle de saída de dados e auditoria.
3. Entregar MCP por STDIO somente leitura, com broker seguro e limites.
4. Validar interoperabilidade com clientes externos, isolamento e recuperação.
5. Implementar runtime independente de provider, eventos normalizados e cancelamento por sessão/turno.
6. Implementar chat, configuração e aprovações nativos, conforme o design system.
7. Implementar OpenAI/Codex com autenticação oficial e capabilities comprovadas.
8. Implementar Claude por API Key, streaming e tool calling.
9. Integrar o provider local preservando ONNX offline e autocomplete.
10. Liberar escritas unitárias e índices somente após aprovação vinculada à operação, auditoria durável e testes reais de concorrência.
11. Concluir segurança, resiliência, distribuição e verificação de dependências.
12. Validar e documentar todos os critérios AC-01 a AC-20.
Restrições:
- Preservar .NET 10/Avalonia, Windows/Linux, pt-BR, nome e licença MIT.
- Respeitar integralmente AGENTS.md e os invariantes de contexto, BSON, cancelamento, sessão e proprietário único do LiteDB.
- Conectar uma conta não autoriza enviar dados; contexto, destino e permissões devem ser explícitos.
- Não expor ferramentas antes de concluir seus gates de segurança.
- Preservar a IDE funcional sem providers, credenciais ou processos externos.
- Não incluir workflows da fase 8, shell livre, scripts arbitrários, subagentes do produto ou persistência de transcripts.
- Se Codex exigir fallback para API direta, registrar a decisão e suas limitações; não declarar login ChatGPT entregue sem comprovação.
Validação e conclusão:
Executar restore locked, build e testes pelos comandos oficiais do repositório. Inspecionar PNGs reais da UI nos dois temas. Validar operações contra MongoDB descartável real, providers com credenciais autorizadas e integração nativa em Windows/Linux.
Manter rastreabilidade entre requisitos, implementação, testes e evidências. Atualizar ADRs, catálogo funcional, design system, guia, matriz de validação e acompanhamento conforme as mudanças.
Não considerar o marco MCP somente leitura como conclusão da versão. Declarar a meta concluída apenas com os critérios de aceite comprovados. Registrar explicitamente bloqueios, testes ignorados e homologações pendentes; mocks e testes headless não substituem evidência real.

## Estado da meta

**Meta ativa; fase não concluída.** O lote 0 tem pesquisas e provas isoladas parciais. O lote 1 tem `ISecretStore` e `IAgentCredentialProvider` em `Application`, `SecretReference` em `Core`, adapters `WindowsCredentialSecretStore` e `LinuxSecretServiceSecretStore` em Infrastructure e composição singleton dos stores e do provider de credenciais no DI da aplicação. O Windows passou testes determinísticos, incluindo falha tipada para UTF-16 inválido; o round-trip nativo foi reconfirmado como **ignorado** por `1312 / NoLogonSession`. O Linux passou testes focados anteriormente, sem D-Bus/Secret Service nativo; **por orientação do usuário, não executar testes no Linux nesta continuação**. O lote 2 tem contratos de sessão/turno e de política/permissão, persistência versionada de grants e ledger de auditoria no proprietário LiteDB existente, evaluator e registry interno de `list_connections`, com deadline não cooperativo por `Task.WaitAsync` e observação de fault tardio. As facetas de política, auditoria e evaluator estão registradas como singletons; os repositories são facetas da mesma instância LiteDB. Composição, evaluator, registry, persistência de autorização e ledger passaram **77 testes focados Windows, 0 ignorados** em 23/09/2026; os casos do registry cobrem TOCTOU de perfil e alias externo que não retransmite o nome livre. O round-trip nativo Windows foi ignorado. Revisão da persistência de autorização não encontrou bypass e registrou que o CAS cobre chamadas pelo mesmo owner, não processos separados/restauração de backup. Revisão da auditoria encontrou uma falha no correlacionamento de intenções que está em correção; schema ainda incompleto frente ao contrato 09. O Tool Registry segue fora do DI porque broker/identidade MCP, integração de auditoria fail-closed e gates de saída ainda faltam. Também faltam migração Mongo, runtime, chat, providers integrados e tool MongoDB exposta. **AC-01 a AC-20 continuam pendentes.** Nenhum SDK dos spikes foi promovido à solução principal. O restante dos lotes 2 a 12 segue pendente ou parcial, sem seus gates completos.

O objetivo integral permanece: um registry único para MCP e chat, políticas de acesso e auditoria por chamada, runtime independente de provider, chat Avalonia, OpenAI/Claude, ONNX local preservado, cofre por sistema operacional e ferramentas MongoDB com leitura inicial e escritas somente após os gates de aprovação. [Escopo e dependências](../phases/phase-07-v0.11.0/10-plano-de-implementacao.md).

## Entregas e evidência já observada

| Frente | Estado verificável | Limite |
| --- | --- | --- |
| Codex App Server | Spike Windows: `codex-cli 0.155.0-alpha.16`, 310 schemas JSON com hashes conferidos; handshake STDIO `initialize`/`initialized` e EOF normal. [Harness](../../eng/spikes/phase-07/codex-app-server/README.md). | A documentação oficial consultada o classifica experimental e sem suporte para produção. Sem prova de login, keyring, confinamento, sessão/turno ou tool. Saídas de schema ficam em `%TEMP%`, fora do repositório. |
| MCP C# | Spike `ModelContextProtocol` 2.2.0: **10/10 testes Windows**, quatro SDK↔SDK e seis com cliente BCL independente em processo separado. Exercita revisões `2025-11-25` e `2026-07-28`, STDIO real, descoberta/listagem sintética vazia e rejeição de versões incompatíveis. [Spike e transcripts](../../eng/spikes/phase-07/mcp-sdk/README.md). | Não é o servidor do produto, suíte oficial de conformidade ou homologação com Claude/Codex; Linux e tools reais pendentes. |
| OpenAI API direta | Spike isolado `OpenAI` 2.14.0: build e **5/5 testes offline** para SSE fragmentado, chamada de função, allowlist e cancelamento. [Spike](../../eng/spikes/phase-07/openai-api/README.md). | Sem conta/chave real ou chamada de rede. Auditoria de vulnerabilidades não concluiu por falha TLS/credenciais do feed NuGet. |
| Anthropic | Spike `Anthropic` 12.50.0: restore/build, assinatura e inventário; contrato de streaming compila. [Spike](../../eng/spikes/phase-07/anthropic-sdk/README.md). | Sem stream observado, chamada API, credencial, tool calling real ou Linux. |
| Cofres de SO | Adapter Windows implementa leitura/gravação/remoção via Credential Manager, disponibilidade sem escrita, limpeza de buffers e falha tipada para UTF-16 inválido. [Código](../../src/EsilvaSoft.SlopStudio.Infrastructure/WindowsCredentialSecretStore.cs) · [testes](../../tests/EsilvaSoft.SlopStudio.UnitTests/WindowsCredentialSecretStoreTests.cs). O round-trip nativo foi ignorado com `1312 / NoLogonSession`. `LinuxSecretServiceSecretStore` e seu protocolo/criptografia têm testes focados. [Spike Linux](../../eng/spikes/phase-07/linux-secret-service/README.md). | Stores e `IAgentCredentialProvider` estão compostos como singletons no DI da aplicação. Windows não foi validado com gravação nativa neste processo; Linux não teve runtime D-Bus/Secret Service. Isso não substitui homologação nativa. |
| Contratos de credenciais | `ISecretStore`, `IAgentCredentialProvider` e falhas tipadas em `Application`; `SecretReference` opaca/versionada em `Core`; composição singleton verificada. A execução focada final dos cofres, política e registry passou com **142 testes aprovados e 1 ignorado**. | Sem UI, referência persistida, migração de perfis ou resolução por provider externo. `Cancelled` representa prompt dispensado; cancelamento pelo chamador usa o token. |
| Políticas e registry | Persistência versionada de políticas/grants no proprietário LiteDB existente; provider, repository e evaluator registrados no DI como facetas/singletons corretos. Contratos e registry interno de `list_connections`; deadline não cooperativo usa `Task.WaitAsync` e observa fault tardio. **71 testes focados Windows passaram** antes do ledger. Revisão especializada não encontrou bypass na persistência. O registry revalida perfis e usa alias derivado do ID para nomes externos. | CAS é intra-owner e não cobre processos separados/restauração de backup. Round-trip nativo Windows do cofre foi ignorado por `1312 / NoLogonSession`; não houve teste Linux nesta continuação. Ainda faltam destino MCP autenticado, broker, integração de auditoria fail-closed e admission control antes de expor tools; nenhum AC é aprovado. |
| Auditoria de agentes | `AgentAuditEvent` schema fechado v1 e `IAgentAuditRepository` append-only no mesmo owner LiteDB, registrado em DI. Política de retenção planejada 30 dias/10.000 eventos. **6 testes focados Windows**; integrado no filtro conjunto de 77 testes. | Revisão independente encontrou correlação fraca entre intents e desfechos, que pode apagar intent ainda pendente; correção em andamento. Schema não cobre todos os campos de `09-seguranca-e-privacidade.md`; ledger não está ligado ao registry, não é inviolável e não habilita a exposição de tools. |
| Fixtures Mongo reais | Quatro testes `MongoReal` passaram contra MongoDB 8.0.30. Os testes agora encerram processos e removem seus dbpaths temporários em `finally`, inclusive em falha. [Fixture](../../tests/EsilvaSoft.SlopStudio.UnitTests/ConsoleMongoIntegrationTests.cs). | Prova da base existente e da limpeza, não de tools MCP/Agent. |

O [relatório de validação](../phases/phase-07-v0.11.0/17-validacao-da-meta.md) registra versões, comandos, evidência e limites. A [análise do código](../phases/phase-07-v0.11.0/13-analise-do-codigo.md) separa serviços existentes de funções planejadas.

## Decisões e invariantes para continuar

- **Provider inicial OpenAI:** API direta com chave própria do usuário no cofre Slop. Não anunciar login de assinatura ChatGPT como entregue. Codex App Server fica condicional a suporte oficial de produção e prova de confinamento/keyring. Claude embutido usa API Key; não importar sessão Claude Desktop/Code. [Providers](../phases/phase-07-v0.11.0/04-providers.md) · [segredos](../phases/phase-07-v0.11.0/07-autenticacao-e-segredos.md).
- **Cofre:** implementar backend Windows/Linux e falha tipada sem persistir chave em LiteDB, arquivos, logs ou variáveis globais. `SessionConnectionSecretStore` atual é somente memória. A migração Mongo exige inventário, gravação e leitura do novo segredo antes de confirmar referência curta no único proprietário LiteDB, com retomada após queda. Não abrir segunda conexão LiteDB. [Plano de segredos](../phases/phase-07-v0.11.0/07-autenticacao-e-segredos.md).
- **Registry/Mongo:** política `default deny`, principal confiável, grants por conexão/banco/coleção, DTOs allowlist e limites de tempo/bytes. Começar por `list_connections`, `list_databases` e `list_collections` sob `ReadMetadata`; listagem Mongo deve preferir `IMongoMetadataSource`, que solicita `AuthorizedDatabases`/`AuthorizedCollections`. `get_indexes` cru e `get_connection_info` com probe de rede não pertencem ao primeiro recorte. [Catálogo de tools](../phases/phase-07-v0.11.0/06-mcp-tools.md).
- **Entradas externas:** não encaminhar filtros ao `MongoOperationContext.ParseDocument`, pois o caminho interno resolve valores `ENV`. Criar parser Extended JSON literal. Preservar BSON/UUID/Int64; limitar saída sem cortar documentos EJSON. `mongo_find`/`mongo_count` exigem também grants de leitura de documentos e consulta. Nenhuma tool fica visível antes dos gates de segurança. [Lacunas](../phases/phase-07-v0.11.0/13-analise-do-codigo.md).
- **Produto existente:** explorer só navega; seleção não executa consulta nem redireciona abas. Capturar contexto antes de `await`, cancelar por aba/turno sem prometer rollback, preservar opt-outs de rascunho e JSON, tornar falha de persistência visível. UI, sessão e atalhos exigem leitura prévia do [design system](../17-design-system-ui-ux.md) e inspeção de PNGs nos dois temas. Seguir [AGENTS.md](../../AGENTS.md) e a [orquestração da meta](../../agents/goal-orchestrator.md).

## Validação do checkout e limites do ambiente

Em 23/09/2026, após os incrementos de composição singleton, UTF-16 inválido e deadline não cooperativo, a validação integrada passou: `dotnet build EsilvaSoft.SlopStudio.slnx --no-restore -p:UsedAvaloniaProducts=` terminou com **0 avisos/0 erros** e `dotnet test EsilvaSoft.SlopStudio.slnx --no-build --no-restore` terminou com **2.841 UnitTests aprovados, 21 ignorados e 0 falhas; 43 Benchmarks aprovados, 0 ignorados e 0 falhas**. A revisão Astra aprovou as correções P2 de UTF-16 inválido no Windows e deadline não cooperativo do registry. O teste de round-trip nativo do Credential Manager continua ignorado por `1312 / NoLogonSession` e é limite de homologação separado, sem elevar ou validar o total da suíte. Linux segue sem D-Bus/Secret Service nativo exercitado. A execução focada final dos cofres/política/registry passou com **142 aprovados e 1 ignorado**; o spike MCP **10/10** e os quatro testes `MongoReal` repetidos com sucesso permanecem evidências específicas. A persistência versionada de políticas/grants ainda não entra nesta evidência, pois está em andamento.

O restore oficial `--locked-mode` encontrou bloqueio de acesso ao `NuGet.Config` global. Um restore locked alternativo passou com configuração temporária sem rede e cache local, com `NuGetAudit=false` somente nessa execução; isso **não** substitui auditoria de vulnerabilidades. A telemetria externa do build Avalonia foi contornada com `-p:UsedAvaloniaProducts=`, conforme AGENTS.md. Em um teste anterior, 369 dbpaths Mongo descartáveis acumulados consumiram cerca de 95 GB e causaram três falhas por falta de espaço; os diretórios foram conferidos e removidos, a suíte passou na repetição e o fixture recebeu limpeza automática. [Detalhes](../phases/phase-07-v0.11.0/17-validacao-da-meta.md#revalidação-da-solução-base-durante-a-continuação-da-meta).

Não há homologação Linux nativa, provider com credencial autorizada, cliente MCP comercial, leitor de tela, diálogos nativos ou auditoria de distribuição final. Teste headless e handler offline não substituem essas provas.

## Próxima ordem de trabalho

1. Validar os adapters já compostos no DI em Credential Manager Windows e Secret Service Linux nativos. Cobrir Prompt, cancelamento, sessão, bloqueio, reinício, recuperação e isolamento por conta, sem fallback plaintext.
2. Concluir e validar a persistência versionada de políticas/grants pelo proprietário LiteDB existente: classe parcial, gate único, CAS, recuperação, concorrência e fail-closed. Não a registrar como entrega antes do retorno do agente.
3. Integrar referências de segredo e migração versionada pelo proprietário LiteDB existente, com testes de queda, concorrência, recuperação e scan de dados persistidos. Manter separada a migração das credenciais Mongo síncronas atuais.
4. Completar o registry interno com identidade, schemas fechados, autorização, controle de saída e auditoria durável. Só então expor MCP STDIO somente leitura e validar cliente externo/ambos os SOs.
5. Implementar runtime, chat nativo, providers e integração local na ordem do [plano](../phases/phase-07-v0.11.0/10-plano-de-implementacao.md). Liberar escritas apenas após aprovação vinculada à operação e auditoria durável; validar **AC-01..AC-20** com evidência proporcional.

O checkout está na branch `master` e contém alterações **não commitadas** desta execução: documentos da fase, spikes em `eng/spikes/phase-07/`, contratos/testes de credenciais e limpeza dos fixtures Mongo. Conferir `git status` antes de editar; não presumir que os spikes fazem parte da solução principal. Atualizar esta memória quando um gate mudar de estado, registrando data, teste e limite da evidência.
