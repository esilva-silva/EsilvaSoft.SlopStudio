# Segurança, contexto e privacidade

**Proposta — ADR-049/051.** Nenhum dado MongoDB sai da máquina apenas porque um provider está conectado. Uma saída precisa de ação explícita, contexto autorizado ou chamada de tool permitida. O mesmo limite se aplica ao cliente MCP externo: ele é outro destinatário, mesmo quando seu processo está local.

## Contexto não é uma escala de acesso irrestrito

| Opção visível | Payload permitido após autorização | Exclusões |
| --- | --- | --- |
| Nenhum | Mensagem digitada pelo usuário e identificação técnica da sessão | Nenhuma coleta automática; texto digitado também passa por inspeção de segredos |
| Somente metadados | IDs lógicos e nomes de namespaces explicitamente escolhidos | URI, senha, variáveis, documentos, amostras e topologia bruta |
| Schema | Campos/tipos derivados e proveniência da evidência | Valores exemplo; nomes sensíveis devem poder ser omitidos |
| Amostras | Projeção e quantidade autorizadas na coleção escolhida | Amostrar outras coleções ou ampliar projeção silenciosamente |
| Resultados | Página/trecho explicitamente escolhido, com limite | Resultado completo e páginas ainda não carregadas |
| Seleção explícita | Trecho de editor/resultado confirmado, com origem | Arquivo completo, workspace ou histórico por inferência |

Esses escopos são independentes. Selecionar schema não concede documentos; uma inferência remota de schema por amostra exige consentimento para leitura local e envia somente a derivação autorizada. O nome do banco, valores de `distinct`, números de count e explain também podem ser sensíveis. A política descreve dados expostos, não apenas qual botão foi acionado.

`IAgentContextProvider` cria snapshot com origem, conexão/revisão, namespace, aba/versão de texto, data da evidência, classificação, orçamento, redaction e destinatário. Interface mostra prévia do pacote e escopo ativo antes do envio. Tool responses passam pelo mesmo filtro e orçamento; não usar a permissão da primeira mensagem como autorização indefinida para novas coleções.

Trocar provider inicia sessão independente; histórico/contexto anterior não é retransmitido sem confirmação explícita. Trocar Explorer nunca muda conversa ou operação existente. Redução de permissão bloqueia envios futuros e elimina buffers locais que já não devem ser usados; não promete apagar dados já recebidos pelo serviço. Não prometer retenção zero dos serviços externos; apontar termos e controles de conta aplicáveis nas [fontes](15-fontes-e-licencas.md).

## Segredos e fronteiras

Resolver connection ID dentro de Infrastructure. Construir DTO sanitizado por allowlist; jamais serializar `ConnectionProfile` integralmente. Remover senha, URI, certificados privados, tokens, env, caminhos de segredos, usuário/host quando não necessários e mensagens de exceção com credenciais. Nomes de perfil são texto livre: para qualquer destino externo substituir por alias estável derivado do ID (`Conexão <ID>`), não tentar inferir todos os segredos por regex; no destino local o nome real pode ser preservado. Guardar chaves/tokens somente no cofre aprovado; logs de SDK e traces de protocolo não podem capturar autenticação.

O estado local atual não oferece cofre seguro completo. A entrega futura exige migração controlada das credenciais MongoDB que forem persistidas e usadas por agentes, além das novas chaves de IA. Um segredo já gravado não fica seguro só porque deixou de ser retornado por MCP. [Plano de migração e limites de remoção](07-autenticacao-e-segredos.md).

Resposta de banco, schema, nome de coleção, arquivo e mensagem do provider são dados não confiáveis. Nunca reinterpretá-los como política, shell, instrução de aprovação ou conteúdo privilegiado. Redação de padrões é defesa adicional, não substitui seleção de campos e autorização determinística. Se não puder sanitizar com confiança, bloquear com diagnóstico seguro.

## Orçamentos propostos para o primeiro incremento

Configurar limites locais com teto rígido: 100 documentos por find; amostra de documentos padrão 5/teto 20; schema derivado padrão 20/teto 100; 256 KiB por resultado de tool, 64 KiB por chamada, execução padrão 5 s/teto 30 s, até 2 leituras simultâneas por sessão, 4 por conexão e 8 globais, uma escrita por sessão, até 20 tool calls por turno. O catálogo pode impor teto menor; elevar esses valores requer revisão de ameaça e desempenho. Incluir `truncated`, motivo e contagem; nunca cortar dentro de um valor BSON ou apresentar JSON inválido. Documento individual maior que orçamento gera erro sem retornar fragmento enganoso.

O catálogo inicial usa paginação explícita por skip e não promete snapshot consistente nem cursor durável. Se a implementação adicionar cursores, eles deverão ser handles opacos emitidos pelo host, vinculados a principal, destino, consulta, revisão e expiração de até 5 min; sem credenciais/filtros em texto. Revogação invalida handles. Máximo de tokens/custo depende de modelo/conta, com limite configurado e parada por orçamento; sem fallback automático para outro serviço pago. Estes números são decisões iniciais de produto a validar por teste, não limites do MongoDB ou dos fornecedores.

## Auditoria sem conteúdo sensível

Evento versionado contém timestamp UTC, operation/request ID, provider/client, sessão/turno, tool/version, connection ID, namespace autorizado (ou identificador pseudônimo conforme política), nível de risco, decisão e motivo, aprovação local/horário, política/revisão, início/fim, duração, resultado (`Succeeded`, `Denied`, `Cancelled`, `Failed`, `Uncertain`) e contagens. Não contém chave/token, URI, prompt, filtro, diff, resultados, stdout bruto ou stack trace. Hashes de conteúdo sensível também ficam fora do registro persistente.

Persistir via proprietário LiteDB em coleção aditiva/versionada; exportação de auditoria também exige ação explícita. Proposta inicial de retenção: 30 dias, com limite de espaço configurado, sem apagar intenções de escrita ainda não reconciliadas. Auditoria local é rastreabilidade operacional, não prova inviolável contra administrador da máquina. Falha de leitura/escrita do log é visível e fecha o acesso de agentes; a IDE manual continua disponível conforme suas próprias políticas.

Conversas e resultados de tools são efêmeros na v0.11.0; metadados de sessão podem ser retomados sem conteúdo sensível. Não inserir transcript em rascunhos nem histórico de consultas. Retenção própria do provider/App Server é uma fronteira separada a verificar/desativar quando possível; se o provider exige persistência incompatível com a política, a capability de retomada fica indisponível, com explicação.

## Ameaças e mitigação verificável

| Ameaça | Controle | Evidência exigida |
| --- | --- | --- |
| Prompt injection em documento/schema | Dados não elevam política; registry valida toda chamada | Fixture instrui a vazar ENV e nada sai |
| Confused deputy e spoof de conexão | Principal fora de argumentos; allowlist de IDs/namespaces | Cliente A não usa concessão/cursor de B |
| Aprovação reutilizada / TOCTOU | Ticket único + revisão/hash/cancelamento + precondição | Alterar alvo/diff após aprovação nega |
| Exfiltração por consulta e erro | JSON literal, DTO allowlist, erro sanitizado | Canários de secrets ausentes no tráfego/logs |
| Esgotamento de recursos | Limites de tamanho/concorrência/deadline/custo | Payload enorme e stream sem fim terminam |
| Escrita oculta em aggregate | Validação estrutural recursiva e lista fechada | `$out`/`$merge` aninhados e namespaces não concedidos negados |
| Processos nativos do provider | Shell/files/network tools desabilitadas; ambiente mínimo | Nenhum acesso alternativo ao arquivo de workspace/cofre |
| Replay após crash | Sem retry de escrita, intenção auditada e estado incerto | Falha após commit não duplica documento |

Windows/Linux, cofre bloqueado, ausência de D-Bus/Secret Service e ambiente sem internet fazem parte do aceite. Degradação não deve paralisar navegação, consultas manuais, arquivos nem autocomplete determinístico.
