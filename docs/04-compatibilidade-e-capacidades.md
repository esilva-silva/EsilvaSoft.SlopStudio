# Compatibilidade e capacidades

## Política de suporte

Proposta inicial: homologar MongoDB 8.0 como base principal, 7.0 para compatibilidade anterior e 8.2/8.3 em ambientes disponíveis para funções recentes. Isso é uma **matriz de testes pretendida**, não certificação nem afirmação de ciclo de suporte do fabricante. A tabela corrente do driver deverá ser conferida com a versão NuGet fixada nos arquivos de dependências. Versões anteriores somente após demanda e testes específicos. [Compatibilidade oficial](https://www.mongodb.com/docs/drivers/compatibility/), [release notes](https://www.mongodb.com/docs/manual/release-notes/).

| Ambiente | Núcleo de dados | Administração | Observação |
| --- | --- | --- | --- |
| Standalone Community | CRUD, consultas e índices compatíveis | Comandos concedidos ao usuário | Sem transações multidocumento e change streams |
| Replica set Community | Núcleo, sessões, transações e change streams | Saúde/configuração segundo privilégios | Falhas de eleição entram nos testes |
| Cluster sharded | Operações via mongos | Shards, zones e balancer por comando | Restrições de chave, transações, backup e roteamento |
| Enterprise Advanced | Núcleo mais serviços configurados | Autenticação/criptografia/auditoria conforme licença e setup | Não inferir tudo pela edição |
| Atlas | Dados via driver | Controle de recursos via API Atlas | Tier e projeto podem restringir comandos |
| Search/Vector Search próprio | Consultas quando mongot e integração estão disponíveis | Índices conforme versão e implantação | Não tratar como função exclusiva Atlas |

A documentação especializada confirma opções de Search/Vector Search próprias por edição. O anúncio de disponibilidade geral de julho de 2026 cita Community a partir de 8.2 com `mongot`. Uma página geral de estágios ainda contém texto restringindo Vector Search ao Atlas; para esse ponto, usar a documentação especializada e validar no ambiente real. [Search self-managed](https://www.mongodb.com/docs/search/self-managed/current/), [anúncio](https://www.mongodb.com/company/blog/product-release-announcements/mongodb-search-vector-search-now-run-anywhere).

## Modelo proposto

`ServerCapabilities` terá versão do servidor, wire versions, FCV quando acessível, topologia, limites anunciados, identidade do deployment, serviços conhecidos, Stable API configurada e observações de privilégios. A detecção combina eventos do driver, `hello`, `buildInfo`, leitura autorizada de FCV e respostas a consultas de metadados. Nunca executar escrita como teste de capacidade.

Cada operação terá `CapabilityDecision`: `Supported`, `Unsupported`, `Unknown` ou `PermissionDenied`, com motivo, fonte, horário da observação e escopo. A ausência de permissão para `buildInfo` não prova falta de suporte; uma conexão a hostname com aparência Atlas não prova todos os serviços Atlas.

`FeatureDescriptor` deverá conter:

| Campo | Finalidade |
| --- | --- |
| id, category, requirementIds | Rastrear catálogo funcional |
| command/stage/operator, parameters | Descrever entrada e autocomplete |
| minServerVersion, maxVersion, fcvConstraint | Validar faixa e mudanças |
| topology, editions, services, tiers | Expressar pré-requisitos |
| privileges, targetDatabase | Explicar autorização e destino |
| stableApiSupport | Evitar quebra com strict API |
| sideEffects, riskClass, retryPolicy | Governar execução |
| availabilityStatus, deprecation | Separar estável, preview, legado e removido |
| uiCoverage, officialUrl, verifiedAt, testIds | Demonstrar cobertura e evidência |

Não colocar condições de compatibilidade dispersas nos ViewModels. Avaliar de novo no serviço antes de executar e invalidar cache após reconnect, alteração de perfil, mudança de topologia ou erro de autorização. Cache inicial de metadados: cinco minutos, configurável; descoberta cara somente sob demanda.

## Cobertura dos comandos públicos

O inventário por versão será construído a partir dos índices oficiais de comandos e MQL, revisado e versionado no repositório; a aplicação consome uma versão empacotada e funciona offline. `listCommands`, quando permitido, complementa a descoberta, mas não substitui os contratos de privilégios e semântica. Comandos internos não documentados não recebem suporte de produto. [Comandos oficiais](https://www.mongodb.com/docs/manual/reference/command/).

| Família | Destino planejado |
| --- | --- |
| Leitura, escrita, findAndModify e bulk | Editor textual e serviço de dados; formulários não montam consultas |
| Agregação, estágios e expressões | Editor textual completo; apoio visual é complementar e opcional |
| DDL, validação e opções | Explorer e console administrativo |
| Índices convencionais e de busca | Painéis separados com estados de construção |
| Usuários e papéis | Segurança; Atlas por adaptador próprio |
| Replicação e oplog | Diagnóstico básico existente; alterações no backlog sem versão |
| Sharding, zonas, chave e movimentação | Painel no backlog sem versão; runbooks por versão de servidor |
| Diagnóstico, sessões e operações | Monitoramento e ações identificadas |
| Parâmetros, FCV, manutenção e desligamento | Fluxos avançados com impacto e acesso necessários |
| Comandos legados/depreciados | Leitura de scripts, aviso contextual e alternativa |
| Comando novo ainda sem formulário | Console após revisão do descritor; sem rotulá-lo como seguro automaticamente |
| Servidor/OS/cloud fora do protocolo | Integração específica ou runbook com limite declarado |

Critério de completude da baseline: **100% das entradas públicas dos índices das versões homologadas têm classificação e link**, inclusive as excluídas. Isso não equivale a 100% de formulários implementados. O dashboard de cobertura deverá medir separadamente inventário, execução, UI e testes.

## Particularidades obrigatórias

- Stable API é configurável por perfil. `apiStrict` não deve ser desligado silenciosamente para permitir administração. [Stable API](https://www.mongodb.com/docs/drivers/csharp/current/connect/connection-options/stable-api/).
- JavaScript no servidor possui funções depreciadas desde 8.0; novos snippets usarão operadores MQL quando equivalentes. Isso não significa que o JavaScript do cliente `mongosh` tenha sido descontinuado. [JavaScript no servidor](https://www.mongodb.com/docs/manual/core/server-side-javascript/).
- `$queryStats` tem formato declarado instável na referência atual: visualização bruta experimental, sem contrato rígido de parser. [Estágios](https://www.mongodb.com/docs/manual/reference/mql/aggregation-stages/).
- Time series, capped e views não herdam automaticamente as ações de uma coleção comum. [Limites de time series](https://www.mongodb.com/docs/manual/core/timeseries/timeseries-limitations/).
- Não apresentar qualquer produto que implemente parte do protocolo MongoDB como equivalente ao servidor oficial.

## Compatibilidade do modo script

O modo JavaScript + JSON executa no cliente `mongosh`; não depende de `$where`, `$function` ou JavaScript habilitado no servidor. Registrar separadamente versão do shell, servidor e mecanismos de autenticação aceitos pelo processo. Opções do driver C# não são automaticamente transferíveis ao shell: o adaptador deve mapear o perfil e indicar opções não suportadas. Não reduzir TLS ou autenticação para conseguir executar script. [Scripts mongosh](https://www.mongodb.com/docs/mongodb-shell/write-scripts/).

O script tem a conexão/banco da aba como contexto inicial, mas JavaScript geral pode selecionar outros bancos ou abrir conexões. RBAC governa o acesso efetivo; a interface não promete confinamento ao namespace inicial. LiteDB serve apenas à configuração/workspace da IDE e não altera a matriz de suporte MongoDB.
