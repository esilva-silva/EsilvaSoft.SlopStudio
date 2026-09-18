# Pendências de homologação — v0.5.0

A release [v0.5.0](README.md) foi arquivada **por escopo funcional**. Este documento existe porque os gates abaixo continuam abertos. Enquanto ele tiver itens não resolvidos, a v0.5.0 **não pode ser descrita como homologada** em nenhum documento, no README ou na interface.

Fonte: [auditoria do MVP](25-auditoria-mvp-performance.md), [matriz de validação](../../15-matriz-de-validacao.md) e [checklist de homologação](../../16-checklist-homologacao.md).

## Gates abertos

| # | Pendência | Por que não está encerrada |
| --- | --- | --- |
| 1 | **Linux com interface gráfica** | Não há distribuição disponível no host usado nas revisões. A suíte Windows não substitui esse gate. |
| 2 | **Leitor de tela e acessibilidade nativa** | Avalonia.Headless não exercita leitor de tela, foco nativo nem navegação do gerenciador de janelas. |
| 3 | **Diálogos nativos e clipboard em Linux** | O ciclo nativo foi executado apenas em Windows (14/09/2026). |
| 4 | **Startup e responsividade sob carga prolongada** | Existe uma medição nativa inicial; falta série estatística e perfil prolongado. |
| 5 | **Matriz ampliada de autenticação e topologias** | TLS/X.509, SSH, réplicas e sharding não foram exercitados. Servidor standalone local não prova esses ambientes. |
| 6 | **`mongosh` real em Linux** | Somente Windows tem registro de execução. |
| 7 | **Homologação de edição real de documentos** | Releitura e conflito têm testes; a edição em servidor real com permissões variadas não foi homologada. |

## Regra

Nenhum item acima é encerrado por teste automatizado, execução Headless ou contagem de testes aprovados. O encerramento exige evidência no ambiente correspondente, registrada no [checklist](../../16-checklist-homologacao.md) com data.

A reorganização documental de 18/09/2026 **não** fechou nenhum destes gates.
