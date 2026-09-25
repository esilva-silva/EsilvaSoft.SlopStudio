# Fontes oficiais, versões e licenças

Pesquisa documental realizada em **22/09/2026**, abrindo páginas oficiais e arquivos de licença, além de pesquisar os tópicos. Fontes `latest` e branches `main` são móveis; esta lista não fixa dependências do produto. Pacotes candidatos foram restaurados somente em spikes isolados. Até 23/09 nenhum pacote foi adicionado à solução; em 24/09 o lote 3 incorporou apenas `ModelContextProtocol.Core` 2.2.0 no proxy MCP (seção abaixo). Não houve login ou chamada paga. A licença do **EsilvaSoft.SlopStudio permanece MIT**.

## Registro de fontes

| Fonte primária consultada | Evidência usada | Consequência no plano |
| --- | --- | --- |
| [Codex App Server](https://learn.chatgpt.com/docs/app-server) — redirecionamento oficial de developers.openai.com/codex/app-server | Transporte, schema e APIs de sessão/autenticação; a página declara comando e transporte WebSocket experimentais e sem suporte a produção | Não adotar enquanto permanecer sem suporte de produção; exigir decisão/revisão oficial. O spike de handshake não altera essa maturidade |
| [Codex SDK](https://learn.chatgpt.com/docs/codex-sdk) | Distinção SDK/App Server | Avaliar API OpenAI direta como fallback com capabilities reduzidas e sem login ChatGPT |
| [Codex Authentication](https://learn.chatgpt.com/docs/auth) | Login e backend de credenciais | Cofre obrigatório ou memória explícita |
| [OpenAI SDKs](https://developers.openai.com/api/docs/libraries) e [function calling](https://developers.openai.com/api/docs/guides/function-calling) | Cliente API e execução de tools pela aplicação | Baseline inicial API .NET com API Key; política continua no Slop |
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
| OpenAI SDK .NET 2.14.0 | [LICENSE oficial](https://raw.githubusercontent.com/openai/openai-dotnet/OpenAI_2.14.0/LICENSE), MIT; spike isolado com tag/commit, hashes, lock e inventário em [`eng/spikes/phase-07/openai-api`](../../../eng/spikes/phase-07/openai-api/README.md) | Baseline técnica inicial para API direta; cinco testes offline passaram. Sem integração no produto, chamada real ou auditoria de vulnerabilidade concluída; spike configura audit feed dedicado para futura CI conectada |
| Anthropic SDK C# | [LICENSE do repositório oficial](https://github.com/anthropics/anthropic-sdk-csharp/blob/main/LICENSE), texto MIT | **Incorporado em 24/09/2026 (lote 8)** como `Anthropic` 12.50.0 no adapter Claude, ver tabela abaixo |
| Claude Agent SDK Python | [LICENSE](https://github.com/anthropics/claude-agent-sdk-python/blob/main/LICENSE), MIT | Wrapper permissivo não resolve licença do runtime incorporado |
| Claude Agent SDK TypeScript | [LICENSE.md](https://github.com/anthropics/claude-agent-sdk-typescript/blob/main/LICENSE.md), direitos reservados e uso sujeito a termos comerciais Anthropic | Não classificar como MIT nem adicionar dependência comercial automaticamente; adoção exige decisão específica após revisão |
| MCP C# SDK | [LICENSE](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/LICENSE), transição MIT → Apache-2.0 com contribuições legadas MIT | Conferir pacote/tag e todos os avisos; não rotular todo o candidato simplesmente MIT. **Incorporado em 24/09/2026 (lote 3)** somente como `ModelContextProtocol.Core` 2.2.0, ver tabela abaixo |
| `Tmds.DBus.Protocol` 0.94.1 (Linux Secret Service) | MIT; commit/tag e hashes registrados no [spike de viabilidade](../../../eng/spikes/phase-07/linux-secret-service/README.md) e lock Desktop | Candidato de baixo nível, sem prova de runtime D-Bus. `libsecret` não foi selecionado; SBOM/licenças por RID continuam gate |
| Node/Python/libsecret e bibliotecas nativas | Não adicionados; seleção concreta pendente | SBOM e licenças por versão/arquitetura são gate antes de empacotar |

## Dependência incorporada no lote 3 — proxy MCP

Em 24/09/2026 o projeto `EsilvaSoft.SlopStudio.McpServer` (executável separado, sem referência a Infrastructure, LiteDB ou MongoDB.Driver) passou a referenciar o pacote abaixo, com versão central em `Directory.Packages.props` e locks `packages.lock.json`, `packages.WinML.lock.json` e `packages.Cuda.lock.json` do projeto (conteúdo idêntico). Nenhum outro projeto da solução recebe esses pacotes. O grafo é o mesmo do [spike](../../../eng/spikes/phase-07/mcp-sdk/README.md), exceto por usar só `ModelContextProtocol.Core` (sem hosting/DI/caching).

| Pacote | Versão | Tipo | Licença declarada | contentHash (lock NuGet) |
| --- | --- | --- | --- | --- |
| ModelContextProtocol.Core | 2.2.0 | Direta | Apache-2.0; LICENSE do commit `6fa3825` documenta contribuições legadas MIT e docs CC-BY-4.0 ([cópia](../../../eng/spikes/phase-07/mcp-sdk/licenses/ModelContextProtocol-2.2.0-LICENSE)) | `FeBfXU6T8k+jw4afg4sfxdEX2rL/e5oKOk9ROOGztu9k47+7Bz08sdaToYt2XvMY1opNbwxYQOFMj6wH9TInhA==` |
| Microsoft.Extensions.Logging.Abstractions | 10.0.10 | Direta (`VersionOverride`; o pin central 10.0.0 segue nos demais projetos) | MIT | `zkFxGYUvdxAvIKTyXHrmW+Sux53D4SezD9dMyZ6hrwwzPQJNuwCRy1f5W7AvYTqacEGhWF2XderRQG1OvbV8og==` |
| Microsoft.Extensions.AI.Abstractions | 10.8.3 | Transitiva | MIT | `K0B05oApxmviWalNHPMBBcRC7erKiDATz3ENNR/jqTR9JwIwLRefgDhj2jCRwL1aca99pXUe0qyQC73/xIuZig==` |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.10 | Transitiva | MIT | `z/2xXlFw2aLGjHyEm6E0tQ+In6VfzQzTrtArbQ2c0TQE16ZbyDCMGPvaUT9I0s8rgy9sRWlU2P9waW37qV04qA==` |

Nenhum binário nativo no grafo (inventário do spike). Pendências antes de distribuir: incluir LICENSE Apache-2.0/avisos MIT em `THIRD-PARTY-NOTICES.md` e no pacote, SBOM por RID e consulta de vulnerabilidades conectada. A licença do Slop continua MIT.

## Dependência incorporada no lote 8 — adapter Claude

Em 24/09/2026 o projeto `EsilvaSoft.SlopStudio.Infrastructure.Agents` (pasta `Anthropic/`, compartilhado com o adapter OpenAI do lote 7) passou a referenciar o SDK C# oficial da Anthropic, com versão central em `Directory.Packages.props` e locks `packages.lock.json`, `packages.WinML.lock.json` e `packages.Cuda.lock.json` idênticos (projeto e testes `EsilvaSoft.SlopStudio.Infrastructure.Agents.Tests`). Versão, origem e assinatura são as do [spike](../../../eng/spikes/phase-07/anthropic-sdk/README.md): release `Anthropic-v12.50.0`, commit `2beeb9f9b402b1cdb8030e7cdc38cc6cae5d42aa`, SHA-256 do `.nupkg` `a6cf2fa227e35fa4852d9ba448cbdd7031a4499bf8395177d897bb24c919448b` (reconferido no cache NuGet), assinatura de repositório nuget.org.

| Pacote | Versão | Tipo | Licença declarada | contentHash (lock NuGet) |
| --- | --- | --- | --- | --- |
| Anthropic | 12.50.0 | Direta | MIT ([cópia da tag](../../../eng/spikes/phase-07/anthropic-sdk/licenses/Anthropic-12.50.0-LICENSE.txt)) | `BBtKetn0gsVeaFfRHcqW5hhUwWdNuIp4lTCvrd4roN6HivnJqrKBbEhPWjA/a5eypg2rvGdUr/Z5qzV6Fr7+Bg==` |
| Microsoft.Extensions.AI.Abstractions | 10.5.1 | Transitiva | MIT ([cópia](../../../eng/spikes/phase-07/anthropic-sdk/licenses/Microsoft.Extensions.AI.Abstractions-10.5.1-LICENSE.txt)) | `nB0l3IlsVbNeUxsE7nZNb9GY8rP1OSeqpj0pseORPejt474HNKEauTWTLdSJPkSvPlRMxowZNMo6nyp7APAiSg==` |

`System.Net.ServerSentEvents` e `System.Text.Json` declarados no nuspec são podados pelo SDK .NET 10 (parte do framework). `dotnet list … package --vulnerable --include-transitive` do projeto, em 24/09/2026 com a fonte nuget.org: **nenhum pacote vulnerável** (observação datada, não garantia). Os tipos do SDK ficam `internal` ao adapter; o domínio, o runtime e a UI não o referenciam.

Risco registrado: a transitiva `Microsoft.Extensions.AI.Abstractions` 10.5.1 é superior à 9.8.0 resolvida pelo ONNX GenAI em `UnitTests`/Desktop. Por isso os testes do adapter estão em projeto próprio; qualquer projeto que referencie `Infrastructure.Agents` junto do ONNX local passa a unificar em 10.x e precisa de revalidação offline do ONNX (AC-16) antes da composição no Desktop. Pendências antes de distribuir: aviso MIT em `THIRD-PARTY-NOTICES.md` e no pacote, SBOM por RID. Termos da Claude API, cobrança e disponibilidade de modelo são da conta do usuário; a licença do SDK não autoriza reutilizar sessão claude.ai/Claude Code.

Licença de código permite determinados usos do software; termos do serviço regulam autenticação, acesso e cobrança. Uma biblioteca permissiva não autoriza reutilizar assinatura ou tokens. A presença de termos comerciais em um candidato não muda a MIT do Slop, mas impede sua inclusão automática conforme governança do repositório. A alternativa API permite manter esse candidato fora do incremento inicial.

Para candidatos MIT, preservar copyright/permissão na distribuição, inclusive uso comercial; para componentes Apache-2.0, conservar licença/atribuições e NOTICE aplicável, identificar modificações e revisar condições de patentes. Pacotes MCP e Anthropic foram resolvidos e inventariados em spikes isolados; da árvore MCP, só `ModelContextProtocol.Core` 2.2.0 e suas transitivas entraram no proxy (lote 3). Não existe ainda árvore transitiva final certificável para produto/distribuição: a avaliação é condicional e exige examinar NuGet/npm/Python lock, binário Codex e bibliotecas nativas da versão efetivamente selecionada, por RID. O aceite do lote 0 deve registrar cada dependência transitiva, licença e obrigação, além dos termos de uso da API/conta. Não substituir essa verificação por supor que transitivas herdam a licença do SDK.

## Gate de dependências

Antes de alterar `Directory.Packages.props`, lockfiles ou instalador: selecionar versão publicada específica; registrar origem/tag/hash e maturidade; verificar licença do pacote real, componentes transitivos e nativos por RID Windows/Linux; revisar vulnerabilidades e política de atualizações; gerar SBOM; incluir LICENSE/NOTICE exigidos; validar funcionamento offline da IDE sem os candidatos. Não usar branch `main` como versão reproduzível nem fazer download de executável a partir de texto produzido pelo modelo.

O inventário [THIRD-PARTY-NOTICES](../../../THIRD-PARTY-NOTICES.md) continua representando o que foi incorporado. Esta seção registra somente pesquisa. Homologação de transportes, provider real, cofre, políticas de conta, disponibilidade regional/modelo e direitos de redistribuição do artefato escolhido permanecem gates da implementação, não conclusões desta análise.
