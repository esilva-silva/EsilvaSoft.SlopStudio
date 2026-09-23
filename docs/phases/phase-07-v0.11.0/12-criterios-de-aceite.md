# Critérios de aceite da versão

**Todos pendentes de implementação e evidência futura.** A conclusão desta meta de planejamento não marca nenhum aceite funcional abaixo como aprovado.

| ID | Comportamento exigido | Prova para aprovação |
| --- | --- | --- |
| AC-01 | Conjunto inicial read-only exposto via MCP | TOOL-01/02/05 + catálogo/schema e execução real |
| AC-02 | Cliente externo descobre e executa tools | MCP-01/03; matriz de clientes/versões e trace sanitizado |
| AC-03 | Nenhum segredo MongoDB enviado | PRIV-01; scan canário em todos os canais e DTO allowlist |
| AC-04 | Chat consome runtime independente | Tests de arquitetura e troca de adapter falso/real sem View específica |
| AC-05 | OpenAI/Codex opera pelo adapter | PR-01/02, AU-01/02; sessão/stream/tool/aprovação/cancelamento reais |
| AC-06 | Claude opera pelo adapter | Mesmos contratos e teste API real com credencial autorizada |
| AC-07 | UI oferece apenas autenticação oficial suportada | Revisão das fontes/versionamento e teste de capabilities; login Claude ausente sem autorização específica |
| AC-08 | Chaves/tokens não persistidos plaintext | SK-01/02 e inspeção de cofre, workspace, arquivos do processo e logs |
| AC-09 | Trocar provider preserva UI e boundary | Novo provider de teste registrado sem branch de marca; PRIV-02 |
| AC-10 | Tool calls visíveis no chat | UI-01; estados solicitado/em execução/concluído/falhou/negado com origem |
| AC-11 | Usuário cancela execução isoladamente | RT-02/03 e prova real; sem promessa de rollback |
| AC-12 | Nenhuma ação sem aprovação requerida | APR-01/02, PER-01, AUD-01 e WR-01/02 |
| AC-13 | Conexões por IDs lógicos, contexto explícito | TOOL-01, PRIV-01/02; sem URI em tools/streams |
| AC-14 | MCP e chat usam um registry | Tests de composição/handler e equivalência de políticas/saída |
| AC-15 | Providers desabilitados/removidos não quebram IDE | DEG-01 e startup sem binários/configuração externa |
| AC-16 | IA local continua independente | Teste offline ONNX e fallback atual; nenhum serviço externo requerido |
| AC-17 | Windows e Linux suportados | IPC/cofre/processo/UI/instalação em SOs nativos, versões registradas |
| AC-18 | Roadmap/migração íntegros | Comparação integral dos escopos movidos e links/índice atualizados |
| AC-19 | Escritas unitárias e índices com integridade | MongoDB real descartável, precondição atômica, RBAC, `_id_`, resultado incerto |
| AC-20 | Extensão de providers e licenças verificáveis | Adapter de teste e matriz de SDK/binários/transitivas/NOTICE da release |

## Portas de liberação

Marco MCP read-only exige AC-01/02/03/08/12/13/14 e testes relevantes de AC-17. Chat externo exige também AC-04..11/15/16; versão completa exige todos os critérios aplicáveis, incluindo escritas previstas no lote 10. Tools administrativas, shell, subagents, persistência de transcript e HTTP remoto não são implicitamente liberados por esse aceite.

Se o spike Codex obrigar API direta por impossibilidade de confinamento, registrar formalmente a mudança, capacidades e autenticação reduzidas. Não usar uma API Key para declarar entregue o login ChatGPT. Compatibilidade de um cliente ou plataforma não testada permanece pendente e não pode ser anunciada.

Evidência mínima por aceite: commit/versão, fixture/cenário, SO, SDK/binário/modelo quando aplicável, resultado, artefato sanitizado e limitações. Datas de planos e fontes não são datas de homologação. Testes ignorados e credenciais ausentes devem aparecer explicitamente no relatório.
