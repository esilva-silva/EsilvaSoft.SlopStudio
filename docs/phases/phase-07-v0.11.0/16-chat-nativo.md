# Chat e configuração nativos no Avalonia

**Especificação planejada, sem alteração visual nesta meta.** Segue o [design system](../../17-design-system-ui-ux.md), incluindo localização, tokens, foco, contraste e medidas existentes. Não carregar sites ChatGPT/Claude em WebView.

## Composição e contexto

Painel **Agente IA** recolhível na área de trabalho, sem substituir Explorer ou resultados. Em janela mínima, abrir superfície própria dentro da IDE com retorno ao editor; não reduzir editor/resultados abaixo dos mínimos do design system. A implementação validará a disposição com PNGs antes de fixar dimensões. Cabeçalho: provider/modelo, indicador textual **Local** ou **Externo**, estado e ação Configurar. Contexto fixo aparece como conexão lógica › banco › coleção; mudar seleção no Explorer não o altera.

Histórico virtualizado mostra usuário/agente e cartões de tools com nome legível, destino, risco, estado, duração e erro seguro. Detalhes são expansíveis e não mostram secrets nem resultado completo automaticamente. Área inferior contém mensagem, escopo de contexto com prévia, **Enviar** e **Cancelar execução**. Enviar nunca executa texto MongoDB diretamente. Propostas de código mostram diff e **Aplicar ao editor**, preservando revisão/undo e sem executar consulta; escrita de banco é uma proposta distinta.

A sequência Tab visita cabeçalho, contexto, histórico, mensagem e ações; Shift+Tab faz retorno. Enter insere linha no compositor; Ctrl+Enter envia somente quando o compositor tem foco e identifica esse escopo por dica. Fora dele, atalhos existentes do editor permanecem. Escape fecha popup/modal e devolve foco; não aprova nem cancela uma escrita já enviada silenciosamente. Cancelar execução é ação explícita e indica resultado incerto quando aplicável. Atualização de streaming não rouba foco nem move rolagem se usuário está lendo trecho anterior.

## Configuração orientada por capabilities

Seletor de provider lista os registrados e sua disponibilidade. Autenticação lista somente métodos oficialmente suportados pela versão e política: Codex login ChatGPT/API Key; Claude API Key na baseline; local sem conta. Modelo vem do catálogo permitido e não de lista fixa no domínio. Campos de chave usam entrada protegida; persistir no cofre é escolha explícita, com estado de disponibilidade. Não exibir parte da chave em status. Falha de login/cancelamento/expiração aparece com ação reconectar; não iniciar login só por abrir a tela.

Permissões mostram conexões e namespaces concedidos, leitura de metadados/documentos e operações separadas. Não pré-marcar escrita. O usuário vê que leitura por tool envia dados ao destinatário externo. Mudar provider cria outra sessão e pede escolha explícita se desejar transferir conteúdo; grants não são copiados implicitamente. Configuração por capability evita `if Claude`/`if OpenAI` nos ViewModels de chat.

## Aprovação e estados

Modal proprietária exibe ferramenta, conexão/banco/coleção, filtro/ID, diff, limite afetado, risco e expiração. `Rejeitar` é a ação segura de fechamento; `Aprovar uma vez` só habilita a proposta atual. Confirmar destrutiva exige identificação do destino. O modelo não fecha a modal nem preenche consentimento. Aprovação revogada, destino alterado ou timeout desabilitam confirmar e explicam a razão.

Estados obrigatórios: sem provider, sem modelo local, não autenticado, pronto, conectando, gerando, aguardando tool, aguardando aprovação, cancelando, concluído, resultado incerto, indisponível e erro de persistência. Todos têm texto e ação apropriada, não só cor. Sem IA, o editor permanece operacional. Status global reutiliza o coordenador de operações existente, enquanto o cancelamento continua por sessão/turno.

## Acessibilidade e prova visual futura

Usar recursos semânticos do tema, Inter 13/metadata 12/título 16, código 14, espaçamento múltiplo de 4 e alvos existentes. Status de tool e erro precisam de nomes acessíveis; anunciar progresso em frequência limitada, evitando narrar cada token. Conteúdo longo/CJK e idiomas do produto devem caber com rolagem local. Nenhuma superfície usa cores fixas ou depende apenas de ícones.

Renderizar os estados críticos em claro/escuro, três tamanhos e escalas 100/150/200%; inspecionar PNGs reais, incluindo aprovação longa, erros, contexto truncado e streams. Depois homologar teclado/IME, leitor de tela e diálogos nativos Windows/Linux. Este documento não é evidência visual de UI implementada.
