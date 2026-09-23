# Validação da meta de planejamento

Data: **22/09/2026**. Resultado: plano documental produzido; a v0.11.0 continua **planejada**. Nenhum provider, servidor MCP, projeto, pacote, API Key ou integração de produto foi implementado/adicionado nesta meta.

## Evidência executada

| Verificação | Resultado e limite |
| --- | --- |
| Leitura do pedido anexado e confronto da solução | Nove projetos existentes analisados por responsabilidades, contratos e caminhos; gaps rastreados em [13](13-analise-do-codigo.md) |
| Documentação e roadmap | Inventário dos 91 Markdown externos à nova fase; mapa de preservação em [14](14-migracao-documental.md) |
| Preservação dos escopos | Comparação com `git show HEAD` dos três READMEs movidos: todas as linhas anteriores preservadas após renumeração; workflow conserva bloco original integral com nota técnica adicional |
| Restore | `dotnet restore EsilvaSoft.SlopStudio.slnx --locked-mode` aprovado; primeira tentativa isolada não leu NuGet.Config do usuário, repetição autorizada concluiu |
| Build | `dotnet build EsilvaSoft.SlopStudio.slnx --no-restore -p:UsedAvaloniaProducts=` aprovado, 0 avisos/0 erros; propriedade evita tarefa externa de telemetria conforme AGENTS.md |
| Testes | `dotnet test EsilvaSoft.SlopStudio.slnx --no-build --no-restore`: UnitTests 2.674 aprovados, 20 ignorados, 0 falhas; Benchmarks 43 aprovados, 0 falhas. Isso valida a base existente, não as integrações futuras |
| Revisão independente de contratos | Seis achados corrigidos: grants, tetos de payload, prazo de aprovação, credencial IPC do proxy, cursores futuros e vocabulário/links. Rechecagem documental aprovada |
| Fontes externas | Documentação oficial de providers/MCP/plataformas e licenças abertas; registros datados em [15](15-fontes-e-licencas.md); artefatos/transitivas finais dependem da versão futura selecionada |
| Índice e referências | 109 documentos no snapshot offline, todos comparados integralmente com seus Markdown e processados pelo parser existente; zero destinos locais ausentes em docs, README raiz e avisos de terceiros; referências antigas mantidas somente como histórico explícito |
| Integridade do diff | `git diff --check` aprovado; sem alterações em src/tests, projetos, solução ou lockfiles. Avisos de conversão LF/CRLF do Git não são avisos de compilação |

Não foram executados login, chamadas pagas, MongoDB real, instalação, Secret Service/Credential Manager, servidor MCP nem screenshots de uma UI futura. Os testes ignorados não são evidência positiva de modelo/hardware real. Não houve alteração visual de produto, logo não se reivindica nova homologação de PNGs, leitor de tela ou diálogos nativos. O smoke browser legado (`check-docs-reader.cjs`) não foi usado como prova: seu número fixo de 84 entradas é anterior ao inventário atual; a verificação desta meta compara snapshot/arquivos e links diretamente.

## Auditoria requisito por requisito do pedido

Os números abaixo correspondem às seções do pedido, não a funcionalidades já implementadas.

| Item | Entrega inspecionada | Situação da meta documental |
| --- | --- | --- |
| 1 — Roadmap/migração | 14, índices, catálogo, inventário e fases 7–10 | Preservado e renumerado, incluindo homologação preexistente |
| 2 — Três componentes desacoplados | 02, ADR-046/047/048 | Fronteiras e grafo definidos |
| 3 — Runtime e protocolo interno | 03 | Portas, DTOs, eventos, estados, correlação, filas e cancelamento definidos |
| 4 — OpenAI/Codex | 04/07/15 | App Server priorizado, SDK/API comparados, auth e gates documentados |
| 5 — Claude | 04/07/15 | API C# baseline, Agent SDK avaliado, limitação de login explícita |
| 6 — Providers futuros | 03/04 | DI/capabilities/conformidade; Copilot futuro |
| 7 — IA local | 02/03/04/13 | Reuso ONNX e limites do chat FIM preservados |
| 8 — MCP Server | 05 | Proxy, broker, identidade, lifecycle e recovery |
| 9 — Registry único | 02/03/06/08, ADR-048 | Handler e política comuns, sem implementação duplicada |
| 10 — Tools existentes | 06/13 | Reuso por método, gaps, schemas/saídas e catálogo de liberação |
| 11 — Risco | 06/08 | READ_ONLY/WRITE/DESTRUCTIVE/ADMINISTRATIVE e bloqueios |
| 12 — Permissões | 06/08 | Grants canônicos, escopos, deny default e revalidação |
| 13 — Approvals | 03/08/16 | Proposta imutável, one-shot, validade, precondição e UI confiável |
| 14 — Protocolos distintos | 02/03/05 | MCP versus runtime versus adapter explicitados |
| 15 — Transporte/processo | 05, ADR-047 | STDIO inicial, HTTP opcional, duas eras, Windows/Linux e tradeoffs |
| 16 — Chat Avalonia | 16 | Painel nativo, tools, erros, foco e testes visuais futuros |
| 17 — Seletor/provider/auth | 04/07/16 | UI por capabilities e métodos oficiais reais |
| 18 — Capabilities | 03/04 | Interseção de adapter/modelo/política e extensão sem branches UI |
| 19 — Credenciais | 07, ADR-049 | Cofre por SO, referências, migração/recovery e ausência de fallback plaintext |
| 20 — Conexões protegidas | 06/07/09 | IDs lógicos, resolução local e DTO allowlist |
| 21 — Contexto | 09/16 | Escopos independentes, prévia e orçamento |
| 22 — Privacy boundary | 09, ADR-051 | Conectar não envia dados; tool/ação/consentimento explícitos |
| 23 — Auditoria | 08/09 | Identidade/decisão/desfecho sem conteúdo sensível; falhas visíveis |
| 24 — Arquitetura confrontada | 02/13 | Reuso camadas/proprietário, parser literal e gaps concretos |
| 25 — Projetos | 02/10 | Dois projetos novos propostos, sem fragmentação artificial |
| 26 — Licenças | 15 e THIRD-PARTY-NOTICES | Pesquisa por candidato, SDK versus serviço, gate de transitivas/artefato |
| 27 — Documentação própria | README e 01–17 | Índice navegável e documentos pt-BR |
| 28 — ADRs | ADR-046–051 | Seis decisões não triviais propostas com alternativas/consequências/gates |
| 29 — Etapas executáveis | 10 | Objetivo, dependências, projetos, interfaces/classes, testes e conclusão por lote |
| 30 — Primeiro MCP seguro | 01/05/06/10 | Read-only antes de writes, todos com autorização/auditoria |
| 31 — Testes | 11/12 | Matriz de contratos, falhas, integração real, SO e secrets sem chaves na CI |
| 32 — Degradação | 03/04/07/11/16 | IDE funcional sem providers/rede/modelo/MCP |
| 33 — Aceites | 12 | AC-01..20 com evidência, todos futuros e não marcados aprovados |
| 34 — Resultado da meta | Conjunto completo e relatório presente | Plano e migração entregues; implementação continua em fase futura |

## Referências históricas recuperadas

A varredura encontrou 17 links de código que ainda apontavam para caminhos anteriores à divisão de assemblies; foram direcionados aos arquivos existentes correspondentes. Quatorze links para TRX históricos não presentes no checkout foram convertidos em referências textuais com aviso de indisponibilidade, conservando nome e caminho. Não foram inventados relatórios para substituir esses artefatos. As afirmações históricas mantêm suas datas e não são usadas como prova da nova fase.

## Pendências da implementação, não da redação deste plano

Fixar versões/lockfiles/licenças transitivas no lote 0; provar confinamento Codex e armazenamento real; implementar contratos/handlers/broker/UX; homologar clientes MCP, contas reais, MongoDB, Windows/Linux e acessibilidade. Esses gates estão alocados no plano com critérios observáveis. A aprovação desta documentação não os encerra.
