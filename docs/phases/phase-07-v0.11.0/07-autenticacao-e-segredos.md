# Autenticação e armazenamento de segredos

Estado: projeto, não implementação. Consulta oficial em 22/09/2026. A aplicação continua utilizável com todos os providers desconectados.

## Fluxos permitidos

| Integração | Apresentação inicial | Dono da autenticação |
| --- | --- | --- |
| OpenAI API direta (baseline inicial) | API Key | Slop resolve referência do cofre e entrega a chave somente ao cliente API |
| Codex App Server (condicional) | Login oficial / API Key, conforme capability homologada | Não integrar enquanto experimental e sem suporte oficial para produção; não copiar tokens |
| Claude embutido | API Key | Cliente API oficial; não importar sessão Claude Desktop/Code |
| Local ONNX | Sem login | Nenhuma credencial externa |

O handshake isolado do App Server não libera autenticação: a documentação oficial ainda o classifica experimental e sem suporte para produção. A baseline é API OpenAI direta com chave do usuário guardada no cofre Slop, sem alegar login de assinatura ChatGPT. Se o App Server alcançar suporte oficial de produção, reabrir a decisão e exigir confinamento e keyring comprovados. [App Server — autenticação](https://learn.chatgpt.com/docs/app-server#authentication), [estado de suporte](https://learn.chatgpt.com/docs/app-server).

A configuração Codex deve exigir `cli_auth_credentials_store="keyring"`; indisponibilidade falha de forma visível. `auto` permite fallback plaintext e `file` usa auth.json: ambos são proibidos na integração Slop. `ephemeral` é alternativa explicitamente escolhida de sessão, sem persistência. [Armazenamento oficial Codex](https://learn.chatgpt.com/docs/auth#credential-storage).

Anthropic não autoriza ofertar login claude.ai dentro de aplicações de terceiros/Agent SDK sem aprovação prévia. A possibilidade documentada de o usuário autenticar o binário Claude Code intacto não autoriza o Slop a coletar ou intermediar esses tokens. Portanto o chat embutido oferece API Key; login permanece ausente até autorização e mecanismo oficial específicos. [Agent SDK](https://code.claude.com/docs/en/agent-sdk/overview), [restrições de credenciais](https://code.claude.com/docs/en/legal-and-compliance#authentication-and-credential-use). Nenhuma assinatura garante acesso API ou créditos; cobrança e limites pertencem ao usuário/provedor. O Slop não fornece créditos nem intermedeia cobrança.

## Contratos e propriedade

Propor `ISecretStore` em Application, implementações por SO em Infrastructure. Operações assíncronas `Get`, `Set`, `Delete` e `GetAvailability` aceitam cancelamento; retornam falhas tipadas (`Unavailable`, `Locked`, `Denied`, `Cancelled`, `NotFound`, `Corrupt`) sem ecoar segredo. `Cancelled` cobre prompts de desbloqueio dispensados pelo usuário; timeout e cancelamento da operação continuam distintos. `IAgentCredentialProvider` resolve referências para uso efêmero pelo adapter; nunca integra payload de prompt/evento/auditoria. LiteDB persiste apenas `SecretReference`, provider, método, identificador de conta não sensível e versão. Não guardar fragmentos da chave para identificação.

| Plataforma | Decisão proposta | Gate |
| --- | --- | --- |
| Windows | Credential Manager por usuário; DPAPI CurrentUser somente como implementação explícita revisada | Validar acesso, roaming/backup, exclusão e outro usuário sem acesso |
| Linux | Secret Service do desktop; avaliar binding de baixo nível `Tmds.DBus.Protocol` 0.94.1 (MIT) | Prova somente documental do candidato; runtime D-Bus indisponível neste host. Validar serviço ausente, sessão bloqueada, desbloqueio cancelado e ambiente headless |
| Sem cofre utilizável | Modo somente memória solicitado explicitamente ou provider indisponível | Nenhum fallback para arquivo, LiteDB, variável persistida ou configuração |

Essas escolhas utilizam mecanismos de plataforma, mas sua integração ainda precisa ser implementada e homologada. [Windows Credentials Management](https://learn.microsoft.com/en-us/windows/win32/secauthn/credentials-management), [Secret Service](https://specifications.freedesktop.org/secret-service/latest/).

API Keys persistentes passam pelo cofre Slop. Tokens OAuth/refresh/session gerenciados por Codex permanecem no armazenamento seguro do processo oficial, sem cópia no Slop; essa via não faz parte da baseline enquanto App Server estiver sem suporte de produção. Usar diretório de configuração isolado e verificar efetivamente o backend antes de eventual login; isolamento de pasta sozinho não prova isolamento do item no keyring. Testar que logout não remove credenciais de outra instalação. Se um futuro fluxo fizer Slop proprietário do token, exigir o mesmo `ISecretStore`, rotação atômica e validade/escopo mínimo; não extrair cookies nem inventar OAuth próprio.

## Estado atual e migração

`SessionConnectionSecretStore` implementa `IConnectionSecretStore` com dicionário em memória; não é cofre persistente. O `EnvironmentVault` e suas referências não comprovam criptografia por SO. Inventariar valores legados e perfis antes de migrar; a presença de campo “secret” não deve ser interpretada como proteção em repouso.

Migração proposta, aditiva e versionada pelo único proprietário LiteDB: detectar registros; solicitar destino seguro quando necessário; gravar segredo no cofre; confirmar leitura; salvar referência/versão em transação local curta; só então retirar valor legado. Sem await dentro da transação. Queda entre etapas deve permitir retomada e limpeza posterior de órfãos, sem perder referência válida. Falha deixa registro recuperável e ação explícita; nunca marcar migração concluída silenciosamente ou criar banco vazio. Tratar backups antigos como potencialmente sensíveis; não prometer eliminação física de páginas ou backups ao remover um campo.

Perfis MongoDB continuam resolvidos localmente por ID. Provider e cliente MCP recebem somente metadados aprovados; nunca URI completa, senha, tokens, variáveis de ambiente ou caminho do cofre. Antes de expor qualquer tool, usar DTO de conexão por allowlist em vez de serializar o perfil persistido. Erros MongoDB e mensagens do subprocesso também exigem saneamento.

## Ciclo de vida e falhas

Configuração captura provider/conta/método antes do await. Login usa nonce/correlação e timeout; resposta antiga não conecta uma configuração substituída. Chave inválida não entra em loop de retries. Expiração produz estado “Autenticação necessária”; refresh tem exclusão por conta, prazo e limite. Logout cancela execuções associadas, invalida handles, remove segredo local conforme escolha do usuário e informa falha; remoção local não promete revogação remota.

Nunca passar segredo por argumento CLI, stdout, telemetria, URI ou crash report. Se SDK só aceitar ambiente, injetar apenas no processo filho autorizado, sem herança global; não enviar credenciais MongoDB ao sidecar. Strings em memória .NET não oferecem garantia de apagamento; minimizar cópias e tempo de vida. Nenhum teste ou screenshot deve usar segredo real; CI comum roda fixtures, homologação real é local/ambiente autorizado e identificada separadamente.

Critérios: validar Windows/Linux com cofre disponível/ausente/bloqueado, cancelamento de login, expiração, mudança de conta concorrente, falha de gravação/exclusão, reinício durante migração, opt-out, scan de LiteDB/logs/arquivos temporários e sidecar sem auth.json. Todas as falhas preservam uso normal da IDE.
