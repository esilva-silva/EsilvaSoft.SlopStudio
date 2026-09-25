# Autenticação e armazenamento de segredos

Estado: projeto, não implementação. Consulta oficial em 22/09/2026. A aplicação continua utilizável com todos os providers desconectados.

## Fluxos permitidos

| Integração | Apresentação inicial | Dono da autenticação |
| --- | --- | --- |
| OpenAI API direta (baseline inicial) | API Key | Slop resolve referência do cofre e entrega a chave somente ao cliente API |
| Codex App Server (sublote 7B, condicional) | Login oficial / API Key, conforme capability homologada | Só com suporte oficial de produção ou risco aceito registrado pelo usuário, mais confinamento e keyring provados (ver [decisão de 25/09/2026](#login-por-conta-decisão-de-25092026)); não copiar tokens |
| Claude — modo Anthropic API (lote 8) | API Key | Cliente API oficial; modo separado da assinatura, escolhido explicitamente |
| Claude — modo Claude Code / assinatura (bloco CL, prioritário) | `claude auth login`/`logout` disparados em processo visível; fluxo concluído no navegador pela Anthropic | Binário oficial do Claude Code; Slop só consulta `claude auth status`, sem ler tokens nem arquivos de `~/.claude`. Gates GCL-1..8 em [23](23-integracao-claude.md#gates) |
| Local ONNX | Sem login | Nenhuma credencial externa |

O handshake isolado do App Server não libera autenticação: a documentação oficial ainda o classifica experimental e sem suporte para produção. A baseline é API OpenAI direta com chave do usuário guardada no cofre Slop, sem alegar login de assinatura ChatGPT. Se o App Server alcançar suporte oficial de produção, reabrir a decisão e exigir confinamento e keyring comprovados. [App Server — autenticação](https://learn.chatgpt.com/docs/app-server#authentication), [estado de suporte](https://learn.chatgpt.com/docs/app-server).

A configuração Codex deve exigir `cli_auth_credentials_store="keyring"`; indisponibilidade falha de forma visível. `auto` permite fallback plaintext e `file` usa auth.json: ambos são proibidos na integração Slop. `ephemeral` é alternativa explicitamente escolhida de sessão, sem persistência. [Armazenamento oficial Codex](https://learn.chatgpt.com/docs/auth#credential-storage).

Para Claude, distinguir execução do binário oficial intacto e oferta de login pelo Agent SDK. As fontes atuais permitem a primeira sob condições, enquanto a documentação do SDK mantém exigência de aprovação para ofertar login. O aviso de cobrança não remove essa distinção. A modalidade foi fixada em 25/09/2026: binário oficial intacto como subprocesso, com login concluído pelo fluxo da Anthropic (seção seguinte); o Agent SDK não é usado. [Fontes e revisão datada](15-fontes-e-licencas.md#revisão-de-contas-próprias--25092026). O Slop não coleta credenciais da assinatura nem fornece créditos; autenticação e cobrança permanecem entre usuário e fornecedor.

## Claude Code: autenticação delegada — decisão de 25/09/2026

Decisão vigente para Claude ([ADR-053](../../10-decisoes-arquiteturais.md#adr-053--claude-via-assinatura-usando-o-binário-oficial-do-claude-code-como-subprocesso-25092026), [23](23-integracao-claude.md)): o processo oficial do Claude Code é o único responsável pela autenticação da assinatura. O Slop:

- detecta instalação e estado por `claude --version` e `claude auth status` (JSON; exit 0 autenticado, 1 não), com caminho absoluto validado e sem shell;
- oferece **Login** e **Logout** que executam `claude auth login`/`claude auth logout` em terminal/processo visível; o usuário conclui no navegador; sem `--console` no modo assinatura; `/login` não existe em `-p`;
- não lê stdout em busca de tokens, não lê/copia/apaga arquivos de `~/.claude`, não grava nada da conta no LiteDB ou no cofre Slop e exibe a conta sem e-mail completo;
- **não remove nem injeta** variáveis ou métodos de autenticação. Como `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, `apiKeyHelper` e variáveis de nuvem têm precedência sobre o OAuth da assinatura ([autenticação](https://code.claude.com/docs/en/authentication)), o método efetivo informado por `auth status`/`system/init` é comparado ao modo escolhido; divergência **bloqueia** o envio com aviso e orientação, nunca cobra silenciosamente pela API;
- trata o armazenamento de credenciais do Claude Code como fronteira do processo oficial: o mecanismo por SO é registrado no spike sem ler conteúdo. **A confirmar no spike:** segundo a [documentação oficial](https://code.claude.com/docs/en/authentication), em Windows e Linux o Claude Code guarda a credencial da assinatura em `~/.claude/.credentials.json` (arquivo, não cofre do SO), não em Credential Manager/Secret Service; o Slop não abre esse arquivo para confirmar. Se o mecanismo efetivo não for um cofre do SO, isso fica registrado como **decisão pendente do usuário**, a resolver antes de P7-CL1-02, antes de habilitar login nesse SO (gate GCL-4);
- mantém API Key (modo Anthropic API) no cofre Slop, separada; nunca há troca automática de modo.

Campos exatos de `auth status` e do evento `init` são confirmados no spike P7-CL0-01. Logout local não promete revogação remota.

## Login por conta: decisão de 25/09/2026

Vale para o 7B; para Claude, substituída pela seção "Claude Code: autenticação delegada" acima.

O plano passa a incluir o login por conta OpenAI como **sublote 7B condicional** ([plano](10-plano-de-implementacao.md#sublote-7b--login-por-conta-openai-condicional)); continua sem implementação e sem homologação. Condições para habilitar: suporte oficial de produção ou risco aceito e registrado pelo usuário; confinamento provado (ferramentas nativas de shell e edição de arquivos desligadas, sem shell livre); armazenamento `keyring` verificado (nunca `auto`/`file`, nunca plaintext); login, logout, cancelamento, expiração, revogação, offline e falha de processo com estados tipados; capabilities efetivas e UI sem prometer o que não foi provado. Os tokens permanecem no armazenamento do processo oficial, sem cópia no Slop. API Key continua baseline e fallback. Login não consente envio de dados.

Os testes de login são **manuais**, com contas próprias do usuário e dados sintéticos, conforme o [roteiro 21](21-homologacao-manual-login.md); nunca automatizados com credenciais reais nem executados em CI, e nenhum agente de desenvolvimento recebe credenciais.

**Histórico — prioridade anterior: contas Codex/ChatGPT (7B) e Claude (8B).** Para Claude, substituída pela seção "Claude Code: autenticação delegada" acima. A decisão anterior de excluir assinatura Claude foi substituída pelo [8B condicional](10-plano-de-implementacao.md#sublote-8b--conta-claude-pelo-runtime-oficial-condicional). O binário/runtime oficial conduz login e refresh; Slop não importa tokens/sessões. Comprovar backend seguro do Claude separadamente em cada SO, sem presumir suporte equivalente ao Codex. Incompatibilidade com cofre ou ausência de modo sem persistência de transcript bloqueia a via. API Key é alternativa por escolha do usuário, sem troca automática de cobrança. Ambos os acessos seguem não implementados e não homologados.

## Contratos e propriedade

Propor `ISecretStore` em Application, implementações por SO em Infrastructure. Operações assíncronas `Get`, `Set`, `Delete` e `GetAvailability` aceitam cancelamento; retornam falhas tipadas (`Unavailable`, `Locked`, `Denied`, `Cancelled`, `NotFound`, `Corrupt`) sem ecoar segredo. `Cancelled` cobre prompts de desbloqueio dispensados pelo usuário; timeout e cancelamento da operação continuam distintos. `IAgentCredentialProvider` resolve referências para uso efêmero pelo adapter; nunca integra payload de prompt/evento/auditoria. LiteDB persiste apenas `SecretReference`, provider, método, identificador de conta não sensível e versão. Não guardar fragmentos da chave para identificação.

| Plataforma | Decisão proposta | Gate |
| --- | --- | --- |
| Windows | Credential Manager por usuário; DPAPI CurrentUser somente como implementação explícita revisada | Validar acesso, roaming/backup, exclusão e outro usuário sem acesso |
| Linux | Secret Service do desktop; avaliar binding de baixo nível `Tmds.DBus.Protocol` 0.94.1 (MIT) | Prova somente documental do candidato; runtime D-Bus indisponível neste host. Validar serviço ausente, sessão bloqueada, desbloqueio cancelado e ambiente headless |
| Sem cofre utilizável | Modo somente memória solicitado explicitamente ou provider indisponível | Nenhum fallback para arquivo, LiteDB, variável persistida ou configuração |

Essas escolhas utilizam mecanismos de plataforma, mas sua integração ainda precisa ser implementada e homologada. [Windows Credentials Management](https://learn.microsoft.com/en-us/windows/win32/secauthn/credentials-management), [Secret Service](https://specifications.freedesktop.org/secret-service/latest/).

API Keys persistentes passam pelo cofre Slop. Tokens OAuth/refresh/session gerenciados por Codex ou pelo runtime Claude permanecem no armazenamento seguro (ou arquivo, no caso do Claude Code — ver acima) do processo oficial, sem cópia no Slop; essas vias só são habilitadas após seus gates específicos (7B/GCL). Usar diretório de configuração isolado e verificar efetivamente o backend antes de eventual login; isolamento de pasta sozinho não prova isolamento do item no keyring. Testar que logout não remove credenciais de outra instalação. Se um futuro fluxo fizer Slop proprietário do token, exigir o mesmo `ISecretStore`, rotação atômica e validade/escopo mínimo; não extrair cookies nem inventar OAuth próprio.

## Estado atual e migração

`SessionConnectionSecretStore` implementa `IConnectionSecretStore` com dicionário em memória; não é cofre persistente. O `EnvironmentVault` e suas referências não comprovam criptografia por SO. Inventariar valores legados e perfis antes de migrar; a presença de campo “secret” não deve ser interpretada como proteção em repouso.

Migração proposta, aditiva e versionada pelo único proprietário LiteDB: detectar registros; solicitar destino seguro quando necessário; gravar segredo no cofre; confirmar leitura; salvar referência/versão em transação local curta; só então retirar valor legado. Sem await dentro da transação. Queda entre etapas deve permitir retomada e limpeza posterior de órfãos, sem perder referência válida. Falha deixa registro recuperável e ação explícita; nunca marcar migração concluída silenciosamente ou criar banco vazio. Tratar backups antigos como potencialmente sensíveis; não prometer eliminação física de páginas ou backups ao remover um campo.

**Estado do lote 1 em 24/09/2026 (implementado, gate parcial).** Perfis Mongo com senha literal são protegidos no salvamento e na migração explícita (`ILegacyConnectionCredentialMigration`): a URI completa vai ao `ISecretStore` com releitura, e `connectionProfiles` guarda só a URI sem senha e `SecretReference` id/versão. Os journals `connectionCredentialMigrations`, `profileCredentialWrites` e `profileCredentialCleanup` (versão de esquema 1; ausência do campo lida como 1; versão desconhecida interrompe a recuperação sem regravar) sobrevivem à queda; `ResumePendingAsync` retoma migrações iniciadas, remove gravações não confirmadas e referências substituídas ou excluídas, devolvendo só contagens. Excluir o último perfil que usa uma referência registra a limpeza na mesma transação. Com senha literal, salvamento, migração e verificação na conexão compartilham uma allowlist fechada de opções de URI. Sem senha literal, só são recusadas as opções que carregam segredo. Não existe fallback plaintext: sem cofre o salvamento falha de forma visível. A composição do owner dispara a retomada em segundo plano; a contagem de pendências fica disponível para a UI por `IConnectionProfileCredentialStatusProvider`. Ainda pendentes: exibir essa contagem na UI; homologação Linux nativa; e eliminação física de páginas antigas do arquivo LiteDB, que não é prometida.

**Identidade do canal.** `IAgentPrincipalAuthority` (Application) é o emissor único de `AgentPrincipal`. O chat nativo recebe um principal interno estável por workspace. Um canal externo é cadastrado por ação local: `agentChannels` guarda IDs opacos, estado e referência. A prova de 256 bits fica só no cofre do SO e é comparada em tempo constante. O principal só é emitido quando há política persistida e fica vinculado à revisão dela. `IsCurrentAsync` falha após revogação ou mudança de política. A revogação vale antes da remoção da prova no cofre; se a remoção falhar, ela fica pendente e pode ser recuperada. O limite de confiança é o usuário do SO: outro processo do mesmo usuário pode ler o cofre. Broker, IPC e ligação ao registry/runtime pertencem aos lotes 2/3/5.

Perfis MongoDB continuam resolvidos localmente por ID. Provider e cliente MCP recebem somente metadados aprovados; nunca URI completa, senha, tokens, variáveis de ambiente ou caminho do cofre. Antes de expor qualquer tool, usar DTO de conexão por allowlist em vez de serializar o perfil persistido. Erros MongoDB e mensagens do subprocesso também exigem saneamento.

## Ciclo de vida e falhas

Configuração captura provider/conta/método antes do await. Login usa nonce/correlação e timeout; resposta antiga não conecta uma configuração substituída. Chave inválida não entra em loop de retries. Expiração produz estado “Autenticação necessária”; refresh tem exclusão por conta, prazo e limite. Logout cancela execuções associadas, invalida handles, remove segredo local conforme escolha do usuário e informa falha; remoção local não promete revogação remota.

Nunca passar segredo por argumento CLI, stdout, telemetria, URI ou crash report. Se SDK só aceitar ambiente, injetar apenas no processo filho autorizado, sem herança global; não enviar credenciais MongoDB ao sidecar. Strings em memória .NET não oferecem garantia de apagamento; minimizar cópias e tempo de vida. Nenhum teste ou screenshot deve usar segredo real; CI comum roda fixtures, homologação real é local/ambiente autorizado e identificada separadamente.

Critérios: validar Windows/Linux com cofre disponível/ausente/bloqueado, cancelamento de login, expiração, mudança de conta concorrente, falha de gravação/exclusão, reinício durante migração, opt-out, scan de LiteDB/logs/arquivos temporários e sidecar sem auth.json. Todas as falhas preservam uso normal da IDE.
