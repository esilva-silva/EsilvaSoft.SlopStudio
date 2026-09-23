# Fontes oficiais, versões e licenças

Pesquisa documental realizada em **22/09/2026**, abrindo páginas oficiais e arquivos de licença, além de pesquisar os tópicos. Fontes `latest` e branches `main` são móveis; esta lista não fixa uma dependência. Nenhum pacote, binário, login ou chamada paga foi adicionado/executado nesta meta. A licença do **EsilvaSoft.SlopStudio permanece MIT**.

## Registro de fontes

| Fonte primária consultada | Evidência usada | Consequência no plano |
| --- | --- | --- |
| [Codex App Server](https://learn.chatgpt.com/docs/app-server) — redirecionamento oficial de developers.openai.com/codex/app-server | Transporte, schema e APIs de sessão/autenticação | Adapter separado e spike da versão escolhida |
| [Codex SDK](https://learn.chatgpt.com/docs/codex-sdk) | Distinção SDK/App Server | Priorizar App Server para integração interativa |
| [Codex Authentication](https://learn.chatgpt.com/docs/auth) | Login e backend de credenciais | Cofre obrigatório ou memória explícita |
| [OpenAI SDKs](https://developers.openai.com/api/docs/libraries) e [function calling](https://developers.openai.com/api/docs/guides/function-calling) | Cliente API e execução de tools pela aplicação | Alternativa API .NET; política continua no Slop |
| [Claude Agent SDK](https://code.claude.com/docs/en/agent-sdk/overview) | Python/TypeScript e restrição de login de terceiros | Sidecar avaliado; não ofertar login de assinatura no chat |
| [Claude legal and compliance](https://code.claude.com/docs/en/legal-and-compliance) | Distinção entre produto terceiro e binário Claude Code intacto | Não coletar/reutilizar tokens de aplicações Anthropic |
| [Claude C# SDK](https://platform.claude.com/docs/en/cli-sdks-libraries/sdks/csharp) | Cliente C# oficial e mudança de origem do pacote | API .NET é opção concreta, separada do Agent SDK |
| [MCP 2026-07-28 — transportes](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports) | STDIO e Streamable HTTP | Fixar revisão explícita e testar compatibilidade |
| [MCP Streamable HTTP](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/streamable-http) e [STDIO](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/stdio) | Segurança e diferenças por transporte | STDIO inicial; HTTP opt-in após gates |
| [MCP C# SDK v2 — transportes](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/transports/transports.html) | Integração .NET e ambiente de subprocesso | Preferir SDK oficial; não herdar ambiente sensível |
| [Windows Credential Management](https://learn.microsoft.com/en-us/windows/win32/secauthn/credentials-management) e [Secret Service](https://specifications.freedesktop.org/secret-service/latest/) | Mecanismos por plataforma | Implementar e testar cofre Windows/Linux |

A página MCP `latest` consultada redirecionou para **2026-07-28**. Essa revisão altera o modelo HTTP (POST por mensagem, metadata por request, sem sessão de transporte/GET persistente; cancelamento fechando stream). Compatibilidade **2025-11-25** deve ser tratada como modo distinto, não combinando semânticas. O plano visa ambas as gerações; a versão concreta do SDK precisa provar suporte, e cada cliente externo terá evidência própria. A presença de documentação v2 não prova disponibilidade/estabilidade do pacote que será escolhido. [Especificação HTTP](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/streamable-http).

## Dependências candidatas, não incorporadas

| Candidato | Licença/evidência consultada | Decisão de distribuição |
| --- | --- | --- |
| Codex executável / Codex SDK | [LICENSE oficial do repositório](https://raw.githubusercontent.com/openai/codex/main/LICENSE), Apache-2.0, consultado nesta revisão; confirmar artefato/tag no spike | Uso comercial e redistribuição sujeitos a licença/avisos, indicação de modificações e NOTICE quando aplicável. Não presumir cobertura de serviço, modelo ou componente nativo; binário externo homologado primeiro |
| OpenAI SDK .NET | [LICENSE oficial](https://raw.githubusercontent.com/openai/openai-dotnet/main/LICENSE), MIT, consultado nesta revisão | Permite uso comercial/redistribuição com copyright e licença preservados; alternativa sem pacote selecionado nesta meta |
| Anthropic SDK C# | [LICENSE do repositório oficial](https://github.com/anthropics/anthropic-sdk-csharp/blob/main/LICENSE), texto MIT | Candidato baseline; fixar tag, publisher, hash e transitivas |
| Claude Agent SDK Python | [LICENSE](https://github.com/anthropics/claude-agent-sdk-python/blob/main/LICENSE), MIT | Wrapper permissivo não resolve licença do runtime incorporado |
| Claude Agent SDK TypeScript | [LICENSE.md](https://github.com/anthropics/claude-agent-sdk-typescript/blob/main/LICENSE.md), direitos reservados e uso sujeito a termos comerciais Anthropic | Não classificar como MIT nem adicionar dependência comercial automaticamente; adoção exige decisão específica após revisão |
| MCP C# SDK | [LICENSE](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/LICENSE), transição MIT → Apache-2.0 com contribuições legadas MIT | Conferir pacote/tag e todos os avisos; não rotular todo o candidato simplesmente MIT |
| Node/Python/libsecret e bibliotecas nativas | Não adicionados; seleção concreta pendente | SBOM e licenças por versão/arquitetura são gate antes de empacotar |

Licença de código permite determinados usos do software; termos do serviço regulam autenticação, acesso e cobrança. Uma biblioteca permissiva não autoriza reutilizar assinatura ou tokens. A presença de termos comerciais em um candidato não muda a MIT do Slop, mas impede sua inclusão automática conforme governança do repositório. A alternativa API permite manter esse candidato fora do incremento inicial.

Para candidatos MIT, preservar copyright/permissão na distribuição, inclusive uso comercial; para componentes Apache-2.0, conservar licença/atribuições e NOTICE aplicável, identificar modificações e revisar condições de patentes. Não há pacote resolvido nesta meta, logo não existe árvore transitiva final certificável: a avaliação é condicional e exige examinar NuGet/npm/Python lock, binário Codex e bibliotecas nativas da versão efetivamente selecionada, por RID. O aceite do lote 0 deve registrar cada dependência transitiva, licença e obrigação, além dos termos de uso da API/conta. Não substituir essa verificação por supor que transitivas herdam a licença do SDK.

## Gate de dependências

Antes de alterar `Directory.Packages.props`, lockfiles ou instalador: selecionar versão publicada específica; registrar origem/tag/hash e maturidade; verificar licença do pacote real, componentes transitivos e nativos por RID Windows/Linux; revisar vulnerabilidades e política de atualizações; gerar SBOM; incluir LICENSE/NOTICE exigidos; validar funcionamento offline da IDE sem os candidatos. Não usar branch `main` como versão reproduzível nem fazer download de executável a partir de texto produzido pelo modelo.

O inventário [THIRD-PARTY-NOTICES](../../../THIRD-PARTY-NOTICES.md) continua representando o que foi incorporado. Esta seção registra somente pesquisa. Homologação de transportes, provider real, cofre, políticas de conta, disponibilidade regional/modelo e direitos de redistribuição do artefato escolhido permanecem gates da implementação, não conclusões desta análise.
