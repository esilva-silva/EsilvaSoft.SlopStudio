# Providers e integração externa

Estado: proposta de implementação, pesquisa oficial consultada em 22/09/2026. Spikes isolados MCP, OpenAI e Anthropic foram concluídos como contratos mínimos; nenhum provider externo foi integrado ou homologado no produto. As referências verificadas e os gates de licença estão em [fontes](15-fontes-e-licencas.md).

## Decisão de integração

| Adapter proposto | Caminho inicial | Alternativa avaliada | Decisão e custo |
| --- | --- | --- | --- |
| `OpenAiAgentProvider` | API OpenAI direta pelo SDK C# oficial, com API Key do usuário guardada no cofre Slop | Processo Codex App Server por STDIO; Codex SDK TypeScript | Selecionada como baseline proposta porque o App Server está documentado como experimental e sem suporte para produção. A API direta não oferece login ChatGPT nem o ambiente Codex; capabilities ficam limitadas às capacidades API/modelo verificadas e às tools do registry Slop. O adapter SDK 2.14.0 passa contrato local de streaming/function calling; autenticação e provider real seguem pendentes. |
| `ClaudeAgentProvider` | Claude Messages API via SDK C# oficial | Claude Agent SDK Python/TypeScript em sidecar | API .NET reduz dependências e mantém o loop de tools em Application; sidecar é evolução condicionada, não requisito do primeiro incremento |
| `LocalAgentProvider` | Serviços ONNX existentes | Outro runtime local futuro | Preserva inferência offline e o assistente de propostas atual |
| `CopilotAgentProvider` | Futuro, sem implementação nesta fase | Avaliação oficial própria | Não herda protocolos, licenças ou autenticação dos outros adapters |

OpenAI documenta App Server para clientes próprios com autenticação, histórico, aprovações e eventos, mas a página atual diz que o comando App Server e o transporte WebSocket são experimentais e não suportados para produção. Por isso, a baseline desta fase escolhe API OpenAI direta com tools do registry Slop; o spike de handshake não aprova adotar o App Server. Essa rota exige API Key guardada pelo cofre, não dá acesso de assinatura ChatGPT e não preserva estado/login Codex. O pacote oficial .NET `OpenAI` 2.14.0 compila Chat Completions streaming/function calling sem supressões do diagnóstico experimental; spike local sintético não homologa API/modelo real. MCP é transporte de tools, não o protocolo do chat. [App Server](https://learn.chatgpt.com/docs/app-server), [SDK/API oficial .NET](https://developers.openai.com/api/docs/libraries), [function calling](https://developers.openai.com/api/docs/guides/function-calling).

O SDK C# oficial da Anthropic usa o pacote `Anthropic` a partir da família 10; versões antigas com o mesmo nome tinham outra origem. A seleção exige verificar identidade, release e lockfile, não apenas o nome NuGet. [SDK C# oficial](https://platform.claude.com/docs/en/cli-sdks-libraries/sdks/csharp).

## Codex

O App Server oferece STDIO e schema gerado pela versão do executável, mas seu comando continua classificado como experimental e sem suporte de produção na fonte consultada. A rota permanece excluída da baseline e do código distribuído até que o status oficial e confinamento sejam novamente avaliados. O spike só prova handshake local de uma versão Windows, não threads/turnos, autenticação ou sandbox. [App Server](https://learn.chatgpt.com/docs/app-server).

Se uma futura revisão reconsiderar o App Server, exigir versão suportada, caminho absoluto validado, argumentos estruturados, ambiente mínimo, diretório de trabalho sem workspace MongoDB, limites de frame/fila, stderr saneado e prova de que shell/arquivos/rede nativos estão desabilitados sem bypass do registry. Não importar plugins, skills, MCPs nem configurações globais do usuário. Sem essas provas, manter o provider direto; nunca habilitar execução irrestrita para uma demonstração.

O processo e seus arquivos de sessão são parte da fronteira de privacidade: definir retenção, opt-out, exclusão e permissões por usuário antes de aceitar dados MongoDB. Uma tool via MCP interno ou chamada traduzida chega ao mesmo registry, com identidade vinculada; jamais chamar MongoDB diretamente do adapter. Propostas de alteração de arquivo ficam desabilitadas inicialmente. A aprovação nativa do provider não substitui a aprovação de domínio.

## Claude API e Agent SDK

O caminho API mantém sessões lógicas, histórico autorizado e o loop limitado de tools em Application. O adapter traduz streaming de texto e argumentos; só emite `ToolRequested` após mensagem completa e schema validado. O runtime executa a tool autorizada e devolve seu resultado limitado ao provider. Definir orçamento por execução, número máximo de ciclos/chamadas, timeout e encerramento quando o provider não progride. Interromper a requisição HTTP encerra a entrega local; não promete estorno nem rollback remoto.

O Agent SDK oferece loop de agente e integração com MCP em Python e TypeScript. Foi avaliado, mas adiciona runtime, processo e ferramentas nativas ao pacote desktop. [Agent SDK](https://code.claude.com/docs/en/agent-sdk/overview). O spike opcional deve demonstrar Windows/Linux, interrupção, streaming, retomada, política de ferramentas, callbacks de aprovação, isolamento de diretório/ambiente e ausência de segredos nos logs. A ponte teria JSONL próprio versionado e traduziria somente eventos internos; morrer o sidecar encerra sua execução, sem afetar MongoDB ou ONNX. Não lançar Node/Python do PATH arbitrário nem instalar dependências durante uma conversa. O wrapper Python ter MIT não torna o binário incorporado ou SDK TypeScript automaticamente MIT.

## Capabilities e modelos

Capacidade efetiva = suporte comprovado do adapter/modelo ∩ política do produto ∩ permissão da sessão. Registrar disponibilidade e motivo de indisponibilidade; UI consome o descritor, sem condicionais por fornecedor.

| Capacidade | OpenAI API inicial | Claude API inicial | Local inicial |
| --- | --- | --- | --- |
| Conversa, sessão lógica, cancelamento | Sim, após homologação | Sim, loop Slop | Propostas existentes; conversa depende do modelo/adapter |
| Streaming | Eventos traduzidos | Eventos traduzidos | Só anunciar incremental quando comprovado; fallback atual entrega um bloco |
| Tool calling | Registry controlado | Loop Slop + registry | Desabilitado até validar modelo e parser |
| MCP | Ponte ao servidor Slop | Registry direto; MCP não é pré-requisito do SDK API | Não requerido |
| Model selection | Catálogo permitido pela conta | Catálogo/configuração validada | Catálogo e papéis ONNX existentes |
| Authentication | API Key no cofre Slop; sem login ChatGPT/Codex | API Key | Nenhuma conta externa |
| FileEditing, CommandExecution, SubAgents | Desabilitadas no primeiro incremento | Desabilitadas | Desabilitadas |
| Thinking | Apenas informações públicas suportadas; nunca exigir raciocínio privado | Idem | Não presumir suporte |

Não fixar um modelo comercial no domínio. Guardar seleção por configuração; se desaparecer ou faltar autorização, pedir nova escolha e manter IDE operacional. Não substituir silenciosamente por outro provider nem enviar contexto a ele. A troca de provider cria outra sessão, sem transportar conversa ou consentimentos automaticamente.

## Reuso local e novos adapters

`LocalModelAiChatService` já produz propostas revisáveis usando `IAiChatService`, e `ILocalModelRuntime.StreamAsync` admite implementação que entrega um único bloco. Reusar `ILocalAiModelService`, catálogo, prioridades e cancelamento; criar fachada para eventos de runtime sem declarar tool calling universal. O assistente existente continua disponível sem rede, conta, MCP ou modelo externo.

Um provider futuro registra factory e descritor em DI, implementa sessão e tradução de eventos, valida credenciais/capabilities, executa a suíte comum e documenta licença. Core/Agents e Application/Agents não referenciam seus DTOs. Implementações externas ficam em `Infrastructure.Agents`, com namespaces separados; a UI permanece consumidora do runtime. Não incluir `LocalAi.Core` como requisito de providers externos.

## Gate antes da implementação principal

Provar sessão concorrente, deltas fora de ordem, cancelamento de uma execução sem afetar outra, crash do processo, reconexão sem duplicação de tool, erro de autenticação e indisponibilidade. Registrar versão de SO/binário/SDK/modelo e conta de teste, com dados sintéticos. Resultados reais dependem de credenciais fornecidas para homologação; fixtures simuladas não satisfazem esse gate. Instalação e atualização de sidecars exigem hashes, licença e confirmação de origem; não fazem parte desta meta documental.
