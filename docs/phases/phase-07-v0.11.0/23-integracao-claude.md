# Integração Claude — prioridade da Fase 7

**Plano aprovado pelo usuário em 25/09/2026; nada implementado nem homologado por este documento.** Substitui o sublote 8B como forma de executar a conta Claude e passa a ser o **bloco prioritário** do [plano 10](10-plano-de-implementacao.md#bloco-prioritário--integração-claude-25092026). Nenhum AC é aprovado aqui. O produto exibido ao usuário chama-se KapibaraStudio (`Branding.ProductName`); o repositório e os identificadores continuam `EsilvaSoft.SlopStudio`, abreviado como "Slop" nesta fase.

## Objetivo

Concluir a integração com Claude priorizando a **assinatura Claude Pro por mecanismos oficiais**: o usuário instala o Claude Code, entra com a própria conta pelo fluxo da Anthropic e o chat nativo conversa com o Claude por meio do binário oficial executado como subprocesso. A opção **Anthropic API** (API Key, lote 8) permanece como modo separado. Finaliza-se o que já está aberto (aprovação, broker MCP, configuração) sem abrir frentes desnecessárias.

## Regras do usuário (25/09/2026)

1. Sem engenharia reversa, endpoints privados ou protocolo interno não documentado.
2. Não capturar, copiar, ler ou reutilizar tokens OAuth, arquivos de credencial ou sessões do Claude Code/Claude Desktop.
3. Não transformar a assinatura em API não oficial nem intermediar requisições de outras pessoas.
4. Usar apenas o Claude Code, o Agent SDK ou mecanismo oficialmente documentado. O processo oficial é responsável pela autenticação; o KapibaraStudio não armazena credenciais da conta Claude.
5. Limitações não suportadas são documentadas e visíveis, nunca contornadas.
6. **Anthropic API** (API Key) é modo separado da assinatura. **Nunca há fallback silencioso para API Key** (cobrança diferente). O modo em uso fica sempre visível.
7. Ferramentas nativas do Claude Code liberadas **somente com aprovação por chamada** ([ADR-054](../../10-decisoes-arquiteturais.md#adr-054--ferramentas-nativas-do-claude-code-com-aprovação-por-chamada-25092026)).
8. Continuidade de sessão por `--resume`: o Slop guarda o `session_id` só em memória; o transcript gravado pelo próprio Claude Code em `~/.claude/projects` é limitação aceita e exibida nas configurações.

## Fatos oficiais verificados em 25/09/2026

| Fonte | Fato usado | Consequência |
| --- | --- | --- |
| [Legal and compliance](https://code.claude.com/docs/en/legal-and-compliance) | Terceiros não podem oferecer login Claude.ai nem rotear requisições por credenciais Free/Pro/Max em nome de seus usuários, nem coletar, armazenar ou intermediar credenciais/tokens; o login conclui pelo fluxo da própria Anthropic. Não impede que o usuário final entre no binário **não modificado** do Claude Code com a própria assinatura, inclusive quando uma plataforma o hospeda. Condições: binário intacto; métodos de autenticação embutidos não removidos, desabilitados ou restringidos; cada usuário com a própria credencial, sem intermediar cobrança; "Claude Code" citável em texto simples, não como nome de recurso/produto. Limites anunciados de Pro/Max pressupõem uso individual comum | Arquitetura por subprocesso do binário instalado pelo usuário; nada de login próprio; texto de UI "usa o Claude Code"; risco de uso não individual registrado |
| [Agent SDK — overview](https://code.claude.com/docs/en/agent-sdk/overview) | Salvo aprovação prévia, terceiros não podem oferecer login claude.ai ou limites de uso da assinatura em seus produtos, inclusive agentes do Agent SDK. Não há SDK .NET; outras linguagens executam a CLI como subprocesso com `-p`. Não usar "Claude Code Agent" como marca | Sem Agent SDK embutido; o Slop não "oferece login": dispara o comando oficial que o usuário conclui com a Anthropic. Garantia formal para distribuição ampla fica como risco/pendência (contato comercial) |
| [CLI reference](https://code.claude.com/docs/en/cli-reference) | `claude auth status` (JSON; exit 0 autenticado, 1 não), `claude auth login` (`--console` para cobrança API), `claude auth logout`; `-p`, `--output-format stream-json`, `--input-format stream-json`, `--include-partial-messages`, `--verbose`, `--resume`, `--session-id`, `--permission-mode`, `--permission-prompt-tool`, `--allowedTools`, `--disallowedTools`, `--tools`, `--mcp-config`, `--strict-mcp-config`, `--max-turns`, `--setting-sources` | Contrato de invocação; versão mínima por flag medida no spike |
| [Headless](https://code.claude.com/docs/en/headless) | `--bare` nunca usa a assinatura (só `ANTHROPIC_API_KEY`/`apiKeyHelper`); sem `--bare`, `-p` executa hooks de `.claude/settings.json` e conecta `.mcp.json` do diretório de trabalho sem diálogo de confiança; `system/init` informa modelo, tools, servidores MCP e capabilities (ex.: `interrupt_receipt_v1`, **a confirmar no spike se está presente e com esse nome exato**); SIGTERM deixa o turno inacabado, SIGINT ou interrupt do SDK encerra o turno; `/login` não existe em `-p`; eventos `permission_denied` e `system/api_retry` com categorias (`authentication_failed`, `rate_limit`, `billing_error`…, **lista de categorias a confirmar no spike**, não exaustiva por documentação pública) | `--bare` proibido no modo assinatura; cwd e fontes de configuração controlados; mapeamento de erros tipados |
| [Authentication](https://code.claude.com/docs/en/authentication) | Precedência: variáveis de nuvem, `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_API_KEY`, `apiKeyHelper`, `CLAUDE_CODE_OAUTH_TOKEN`… e por último o OAuth da assinatura | Uma chave no ambiente faz a CLI cobrar pela API. O Slop não remove métodos; **detecta o método efetivo e bloqueia com aviso** quando diverge do modo escolhido |
| [Agent SDK com plano Claude](https://support.claude.com/en/articles/15036540) (16/06/2026) | Mudança de créditos pausada; "Agent SDK, claude -p e apps terceiros" continuam consumindo os limites da assinatura | Sem promessa de gratuidade; limite atingido é erro visível, sem migrar para API |

Campos exatos de `auth status`, `system/init` e dos eventos precisam ser confirmados no spike P7-CL0-01 com o binário instalado; este documento não os presume.

## Arquitetura

```text
IAgentProvider (Application)
├── ClaudeCodeAgentProvider   — modo "Claude (assinatura)" (novo; Infrastructure.Agents/ClaudeCode; identificador interno nunca exibido)
│     └── ClaudeCodeProcessClient → binário oficial `claude` instalado pelo usuário (subprocesso, -p, stream-json)
├── ClaudeAgentProvider       — modo "Anthropic API" (existente; Infrastructure.Agents/Anthropic, API Key no cofre)
├── OpenAiAgentProvider / LocalAgentProvider
└── futuros (mesmo contrato, sem branch por marca na UI)
```

| Elemento | Decisão |
| --- | --- |
| Modos | Dois providers independentes com IDs, descritores e sessões próprios. Trocar de modo cria outra sessão, sem transportar conversa, consentimentos ou grants. Nenhum caminho de código troca de modo automaticamente |
| Autenticação | Novo valor de `AgentAuthenticationMethod` (Core) para "CLI oficial delegada" (nome final pelo architecture-agent, ex.: `OfficialCliDelegated`); `ApiKey` continua exclusivo do modo API. `IsLocal = false` |
| Detecção | `claude --version` e `claude auth status` com caminho absoluto resolvido e validado, argumentos estruturados, timeout curto, sem shell. Resultado tipado: ausente, versão insuficiente, não autenticado, autenticado por assinatura, autenticado por outro método (API Key/ambiente/nuvem) |
| Login/logout | Botões disparam `claude auth login`/`claude auth logout` em terminal/processo **visível** ao usuário; o fluxo conclui no navegador com a Anthropic. O Slop não lê stdout em busca de tokens, não abre nem lê arquivos de `~/.claude` e revalida depois com `auth status`. Sem `--console` no modo assinatura |
| Método efetivo | Comparar o método informado por `auth status` e `system/init` com o modo escolhido; divergência (ex.: `ANTHROPIC_API_KEY` no ambiente) **bloqueia** o envio com aviso e ação orientativa. O Slop não remove nem injeta variáveis de autenticação |
| Turno | `claude -p --output-format stream-json --input-format stream-json --include-partial-messages --verbose --max-turns <limite>`, **sem `--bare`**, `--permission-mode default`; limites de frame/fila/stderr; tradução para `AgentProviderEvent` no adapter; nenhum DTO externo fora de Infrastructure.Agents. `--max-turns` adota um limite configurável (valor exato definido em P7-CL3-01, com base no spike) para evitar loop de turnos internos sem fim |
| Sessão | `session_id` do `system/init` guardado apenas na memória da sessão/aba; turnos seguintes usam `--resume <session_id>`. Reiniciar o app perde a continuidade no Slop; o transcript fica em `~/.claude/projects` (limitação aceita) |
| Permissões | `--permission-prompt-tool` aponta para uma tool MCP do McpServer/broker Slop que chama a **mesma** infraestrutura de aprovação (`AgentWriteApprovalCoordinator`, `AgentRuntimeWriteApprovalBridge`, `AgentApprovalWindow`); ver [ADR-054](../../10-decisoes-arquiteturais.md#adr-054--ferramentas-nativas-do-claude-code-com-aprovação-por-chamada-25092026). A variante interna usada pelos SDKs não é adotada sem documentação pública |
| Tools do produto | `--mcp-config` + `--strict-mcp-config` apontando só para o McpServer Slop; chamadas MongoDB entram no registry único com o principal do canal vinculado à sessão do chat. Regras do registry (grants, aprovação de escrita, auditoria, saída) não mudam |
| Cancelamento | Interrupt documentado de stream-json quando confirmado no spike, senão SIGINT; `kill` só como último recurso e com `OutcomeUnknown` para tool em andamento. Windows não tem SIGINT equivalente: estratégia validada no spike |
| Diretório e configurações | `cwd` escolhido explicitamente (pasta do workspace local ou pasta dedicada), nunca o diretório de dados do app, LiteDB ou cofre; `--setting-sources` restrito e `--strict-mcp-config` para não herdar hooks/MCPs de projeto; valores exatos definidos no spike e no [threat model](22-threat-model.md) |
| Marca | Texto "usa o Claude Code" em rótulos explicativos; o modo é "Claude (assinatura)"; nunca "Claude Code Agent" nem logotipo como nome de recurso |

## Estado real do código (auditoria de 25/09/2026)

| Achado | Arquivo:linha | Tratamento no bloco |
| --- | --- | --- |
| `ClaudeAgentProvider` só API Key (`AgentAuthenticationMethod` = `None`/`ApiKey`), completo offline, sem homologação real (teste `[Explicit]` ClaudeReal) | `Core/Agents/AgentAuthenticationMethod.cs` | Mantido como modo API; homologação manual separada (C-17) |
| Chave recusada fica presa até reiniciar (OpenAI igual) | `Infrastructure.Agents/Anthropic/ClaudeAgentProvider.cs:196-199,231-236` | P7-CL5-03 |
| Configurações sem Testar conexão/Login/Logout; códigos de indisponibilidade não localizados | `Desktop/Agents/DesktopAgentProviderCatalog.cs:65`, `Desktop/ViewModels/AgentProviderOption.cs:38` | P7-CL5-01/02 |
| Estágio de exposição `None` por padrão, sem UI de grants | `Infrastructure/AgentBrokerOptions.cs:27` (`Stage`), `Infrastructure/AgentPlatformOptions.cs:15` (`ToolExposureStage`) | P7-CL4-03 (tools do produto) |
| Coordenador/bridge/interaction authority de aprovação implementados e não ligados; DI usa `FailClosed`; `ApprovalDetails` nulo | `Infrastructure/AgentRuntimeHost.cs:18-31`, `Infrastructure/ServiceCollectionExtensions.cs:139`, `Desktop/App.axaml.cs:88` | P7-CL4-01 (reuso, sem nova infraestrutura) |
| Consentimento de schema com repositório pronto, DI `FailClosed` | `Infrastructure/ServiceCollectionExtensions.cs:120` | P7-CL4-03 |
| Broker MCP não composto no Desktop | composição do App | P7-CL4-02 (pré-requisito de permissões e tools) |
| Folga de timeout do Claude 170 s contra escrita ~165 s | `Infrastructure.Agents/Anthropic/ClaudeAgentProviderOptions.cs:107` | P7-CL5-03; revisar também o prazo de aprovação da CLI no spike |
| Prompt do Claude API afirma "não altera dados" | `Infrastructure.Agents/Anthropic/ClaudeAgentSession.Turn.cs:21` | Ajustar quando tools de escrita forem expostas ao modo API; o modo Claude Code não usa esse texto |

## Passos obrigatórios e tarefas

A ordem abaixo é obrigatória. Uma tarefa só inicia quando suas dependências estão integradas; spikes e testes com CLI falso podem adiantar-se sem habilitar a entrada do produto.

| Passo | Tarefa | Responsável (colaboração) | Dependências | Evidência exigida |
| --- | --- | --- | --- | --- |
| 0 | **P7-CL0-01** Spike reproduzível do CLI instalado: versões, `--version`, JSON de `auth status` (campos, método, tipo de conta), `system/init`, eventos stream-json com e sem `--include-partial-messages`, `--resume`, `--permission-prompt-tool`, `--setting-sources`, `--strict-mcp-config`, interrupt/SIGINT no Linux e alternativa no Windows, detecção de `ANTHROPIC_API_KEY`, versão mínima por flag. Fixtures sanitizadas para testes | agent-provider-agent (persistence-security-agent) | Nenhuma | Script e relatório em `eng/spikes/phase-07/claude-code-cli/`, sem tokens/e-mail; fixtures versionadas; limitações por SO |
| 0 | **P7-CL0-02** Threat model do modo Claude Code ([documento 22](22-threat-model.md); rascunho anterior ao spike, com hipóteses H-01..H-25 a confirmar): ferramentas nativas, hooks, regras `permissions.allow` do usuário, CLAUDE.md do cwd, MCP de projeto, prompt injection, rede, transcript | persistence-security-agent (architecture-agent) | CL0-01 | Documento revisado por code-review; gate GCL-3 |
| 1 | **P7-CL1-01** Detecção do Claude Code (`ClaudeCodeInstallationProbe`) | agent-provider-agent | CL0-01 | Testes com CLI falso: ausente, versão baixa, timeout, saída inválida, caminho não absoluto |
| 2 | **P7-CL1-02** Login/logout oficiais em processo visível | agent-provider-agent (ui-ux-agent) | CL1-01 | Testes: comando/argumentos exatos, nenhuma leitura de stdout de token ou de `~/.claude`; homologação C-03/C-04 |
| 3 | **P7-CL1-03** Validação do estado de autenticação e do método efetivo | agent-provider-agent (persistence-security-agent) | CL1-01 | Máquina de estados com fixtures: autenticado/assinatura, não autenticado, outro método → bloqueio; C-05/C-06 |
| 4 | **P7-CL2-01** Contrato: novo `AgentAuthenticationMethod`, descritor e capabilities do modo | architecture-agent | CL1-03 | Testes de arquitetura/contrato; nenhum DTO externo em Core/Application |
| 4 | **P7-CL2-02** `ClaudeCodeAgentProvider` e composição opt-in | agent-provider-agent | CL2-01 | Provider registrado sem I/O na composição; `IsLocal=false`; IDE sobe sem CLI |
| 5 | **P7-CL3-01** Chat: `ClaudeCodeProcessClient`/`ClaudeCodeAgentSession`, um processo por turno com stream-json | agent-provider-agent (agent-runtime-agent) | CL2-02 | Testes com CLI falso: mensagem, terminal, erro, EOF, crash, stderr limitado |
| 6 | **P7-CL3-02** Streaming com deltas parciais e filas limitadas | agent-runtime-agent (agent-provider-agent) | CL3-01 | Deltas fragmentados/fora de ordem, consumidor lento, stream infinito |
| 7 | **P7-CL3-03** Continuidade por `--resume` com `session_id` em memória | agent-provider-agent | CL3-01 | Resume correto por aba, sessão inválida/expirada, isolamento A/B; C-13 |
| 8 | **P7-CL4-01** Ligar aprovação existente (coordenador/bridge/interaction authority, `ApprovalDetails`) e categorias de permissão | agent-runtime-agent (ui-ux-agent, tool-registry-agent) | CL3-01, GCL-3 | Permitir/Sempre nesta sessão/Negar, destrutiva, expiração, duplicidade; C-08..C-11 |
| 8 | **P7-CL4-02** Tool MCP de permissão no McpServer/broker e composição do broker no Desktop | mcp-integration-agent (persistence-security-agent) | CL4-01 | Canal autenticado vinculado à sessão; tool não chamável pelo modelo; timeout; broker indisponível nega |
| 9 | **P7-CL4-03** Tools do produto via `--mcp-config` + `--strict-mcp-config`; estágio de exposição e consentimento de schema ligados | mcp-integration-agent (tool-registry-agent) | CL4-02 | Mesmo registry/política; cartões de tool; nenhuma tool do Slop/registry acessa MongoDB fora do registry |
| 10 | **P7-CL3-04** Cancelamento por interrupt/SIGINT; kill como último recurso com `OutcomeUnknown` | agent-runtime-agent (agent-provider-agent) | CL3-01, CL0-01 | Cancelar A sem afetar B; cancelar durante aprovação e durante tool; C-12 |
| 11 | **P7-CL5-01** Integração com workspace: escolha explícita do cwd, fontes de configuração, contexto da aba | architecture-agent (ui-ux-agent) | CL3-01, GCL-3 | Cwd nunca no diretório de dados/cofre; C-15 |
| 12 | **P7-CL5-02** UI de configuração e indicação do modo (ver [16](16-chat-nativo.md#configuração-orientada-por-capabilities)) | ui-ux-agent | CL1-02, CL2-02 | PNGs reais claro/escuro 960/1366/1920; foco/teclado; C-07 |
| 13 | **P7-CL5-03** Tratamento de erros: `api_retry`/`permission_denied`/auth/limite/versão/processo; reset da chave recusada nos providers API; folga de timeout | agent-provider-agent (ui-ux-agent) | CL3-01 | Erros tipados e localizados, sem loop nem fallback; C-18 |
| 14 | **P7-CL6-01** Testes: CLI falso determinístico e fixtures do spike; varredura de canários/tokens | qa-testing-agent | CL3..CL5 | Suíte oficial verde; nenhum teste automatizado com conta real |
| 14 | **P7-CL6-02** Revisão independente do diff do bloco | code-review-agent | CL6-01 | Achados resolvidos ou registrados |
| 15 | **P7-CL6-03** Documentação, índice offline e memória; registro da homologação manual | documentation-agent | CL6-02 e registro do usuário | Rastreabilidade AC; nada aprovado sem registro manual |

## Gates

| Gate | Prova | Estado em 25/09/2026 |
| --- | --- | --- |
| GCL-1 Modalidade oficial | **Critério de fechamento (definido em 25/09/2026):** confirmação por escrito da Anthropic autorizando o cenário concreto (binário hospedado por terceiro, uso individual) **OU**, na ausência dela, aceite explícito do usuário restringindo o lançamento a uso individual não distribuído, mais checklist de marca/binário (texto simples "usa o Claude Code", sem logotipo como nome de recurso, binário intacto, métodos de autenticação preservados, credencial própria). Registrar a tensão entre esse critério e a regra "não oferecer login claude.ai": o botão de login não deve simular um formulário Slop, e sim abrir o Claude Code oficial (ver ação "Entrar pelo Claude Code…" em [16](16-chat-nativo.md#configuração-orientada-por-capabilities) e [design system](../../17-design-system-ui-ux.md)) | **Parcial (documental).** Nem a confirmação escrita nem o aceite explícito do usuário foram registrados; garantia formal para distribuição ampla não obtida; risco registrado |
| GCL-2 Spike do CLI | CL0-01 reproduzível, versão mínima por flag, fixtures | Pendente |
| GCL-3 Segurança | [Threat model (22)](22-threat-model.md#condições-para-propor-o-gcl-3) revisado; controles para hooks, `permissions.allow`, MCP de projeto, rede e cwd | **Rascunho existente (documento 22), spike ainda não executado.** Pendente; bloqueia ferramentas nativas |
| GCL-4 Autenticação | Detecção, login/logout delegados, método efetivo com bloqueio, zero tokens no Slop; mecanismo de armazenamento do runtime registrado por SO (a confirmar no spike: `~/.claude/.credentials.json`, sem abrir o arquivo — ver [07](07-autenticacao-e-segredos.md)) | Pendente. Se o runtime não usar cofre do SO, exigir decisão explícita do usuário antes de habilitar nesse SO (decisão pendente antes de CL1-02) |
| GCL-5 Permissões | **Critério verificável (definido em 25/09/2026):** por versão pinada do Claude Code, lista registrada de ferramentas nativas que pedem e que não pedem permissão no modo `default`; controles determinísticos comprovados por teste (`--tools`, `--disallowedTools`, `--settings`/`--setting-sources`) exercitados pelo caso manual C-08; ao menos um caso C por categoria de ferramenta (`ReadFile`, `WriteFile`, `DeleteFile`, `ExecuteCommand`, `Network`, `MCP`, `ExternalTool`); qualquer exceção (leitura sem prompt, regra `permissions.allow`) só é aceita com decisão registrada do usuário, nunca presumida | Pendente |
| GCL-6 Sessão/stream/cancelamento | Testes com CLI falso, concorrência A/B, crash/EOF, eventos tardios | Pendente |
| GCL-7 UI | Configuração e modo em uso com PNGs inspecionados nos dois temas | Pendente |
| GCL-8 Homologação manual | Casos C-01..C-34 do [roteiro 21](21-homologacao-manual-login.md#casos-do-modo-claude-code-assinatura) por SO, pelo usuário, com conta própria | Pendente; nenhuma execução |

Os gates G8B-1..8 do plano anterior ficam substituídos por GCL-1..8. Duas exigências do 8B mudam por decisão do usuário e valem **somente para o modo Claude Code**: ferramentas nativas deixam de ser proibidas e passam a exigir aprovação (G8B-4 → GCL-3/GCL-5); ausência de transcript persistido pelo processo oficial vira limitação aceita e visível (G8B-4 → configurações). O Slop continua sem persistir transcript.

## Evidência exigida

- **Antes de codar adapters:** spike reproduzível do CLI instalado (CL0-01).
- **Automatizado:** somente com CLI falso e fixtures sanitizadas de stream-json; nenhum teste automatizado autentica conta real ou roda em CI com credencial.
- **Homologação:** manual, pelo usuário, com conta própria e dados sintéticos, casos C-01..C-34. Agentes de desenvolvimento não recebem, digitam nem automatizam credenciais.
- Build/testes oficiais de `AGENTS.md` em cada mudança de código; PNGs reais nos dois temas para UI.

## Riscos e limitações

| Risco/limitação | Tratamento |
| --- | --- |
| Termos: garantia formal para distribuição ampla depende de contato comercial com a Anthropic | Registrado como pendência; não afirmar aprovação. Uso individual pelo próprio usuário é o cenário documentado |
| Limites Pro/Max pressupõem uso individual comum | UI não promete volume; limite atingido é erro visível, sem fallback |
| Transcript gravado pelo Claude Code em `~/.claude/projects` | Aceito pelo usuário; aviso nas configurações; o Slop não lê nem apaga esses arquivos |
| Hooks do usuário/projeto executam em `-p` | `--setting-sources` restrito e cwd controlado; o que restar (ex.: configurações gerenciadas) é documentado e exibido |
| Regras `permissions.allow` do usuário podem aprovar fora do diálogo do Slop | Mitigação pelo spike/threat model (fontes de configuração); exceção remanescente exibida ao usuário, nunca omitida |
| Leituras no cwd podem ser permitidas pela CLI sem prompt no modo `default` | Confirmar no spike; se confirmado, avisar e restringir o cwd; não afirmar aprovação de toda leitura |
| `ANTHROPIC_API_KEY`/`apiKeyHelper`/variáveis de nuvem mudam a cobrança | Detectar e bloquear; nunca remover nem injetar variáveis |
| Windows sem SIGINT | Estratégia validada no spike; kill como último recurso com `OutcomeUnknown` |
| Versões mínimas por flag | Medidas no spike; versão inferior exibe "atualize o Claude Code", sem instalar nada |
| CLAUDE.md e arquivos do cwd como vetor de prompt injection | Dados não elevam política; registry e diálogo continuam determinísticos |
| Armazenamento de credenciais do runtime por SO | Registrado sem ler conteúdo; decisão do usuário se não for cofre do SO (GCL-4) |

## Fora do escopo

OAuth próprio, leitura de credenciais, Agent SDK embutido, `--bare` no modo assinatura, fallback para API Key, instalação/atualização automática do Claude Code, WebView, subagentes do produto e persistência de transcript pelo Slop. Alternativas rejeitadas em [ADR-053](../../10-decisoes-arquiteturais.md#adr-053--claude-via-assinatura-usando-o-binário-oficial-do-claude-code-como-subprocesso-25092026).
