# Arquitetura Central de Agentes Especializados

Bem-vindo à pasta central de agentes do **EsilvaSoft.SlopStudio**.

Este diretório concentra todos os agentes de inteligência artificial especializados utilizados no desenvolvimento, manutenção, qualidade, arquitetura e evolução da solução.

---

## 1. Princípio de Especialização

> **Prefira vários agentes pequenos e especializados a um único agente genérico.**

Cada agente neste ecossistema possui um domínio de conhecimento claro, responsabilidades estritamente delimitadas, restrições explícitas e regras de validação próprias.

Nenhum agente opera de forma isolada sem coordenação ou tenta assumir tarefas fora da sua especialidade. A orquestração das metas é realizada centralmente pelo [`goal-orchestrator.md`](goal-orchestrator.md).

---

## 2. Catálogo de Agentes Especializados

| Agente | Arquivo | Responsabilidade Principal | Capacidade Preferida | Capacidade Alternativa |
| :--- | :--- | :--- | :--- | :--- |
| **Goal Orchestrator** | [`goal-orchestrator.md`](goal-orchestrator.md) | Orquestração integral de metas em 15 passos, decomposição, distribuição e acompanhamento de estado. | `reasoning` | `advanced-reasoning` |
| **Architecture** | [`architecture-agent.md`](architecture-agent.md) | Fronteiras de camadas (`Core`, `Application`, `Infrastructure`, `Desktop`), contratos públicos e ADRs. | `reasoning` | `advanced-reasoning` |
| **Code Organizer** | [`code-organizer.md`](code-organizer.md) | Refatoração estrutural mecânica, 1 tipo por arquivo, legibilidade e eficiência de contexto sem alterar comportamento. | `fast` | `balanced` |
| **Code Review** | [`code-review-agent.md`](code-review-agent.md) | Revisão crítica, auditoria de invariantes de `AGENTS.md`, zero warnings e prevenção de regressões. | `advanced-reasoning` | `reasoning` |
| **UI / UX** | [`ui-ux-agent.md`](ui-ux-agent.md) | Interface desktop Avalonia 12.1.2 MVVM, tokens do Design System 17, acessibilidade, temas e testes headless. | `balanced` | `reasoning` |
| **MongoDB Domain** | [`mongodb-domain-agent.md`](mongodb-domain-agent.md) | Integração MongoDB.Driver, BSON puro, UUIDs legados, queries, paginação por cursor, mutações protegidas e agregações. | `balanced` | `reasoning` |
| **Persistence & Security** | [`persistence-security-agent.md`](persistence-security-agent.md) | LiteDB 5.x único via DI em Direct mode, versionamento aditivo, cofre de ambientes, auditoria e proteção de credenciais. | `reasoning` | `advanced-reasoning` |
| **Scripting & Console** | [`scripting-console-agent.md`](scripting-console-agent.md) | Console interativo Jint/Acornima com proxies seguros, runner externo `mongosh` e roteamento multi-conexões. | `balanced` | `reasoning` |
| **Autocomplete & Language** | [`autocomplete-agent.md`](autocomplete-agent.md) | AST tolerante a erros, inferência de schema em queries/agregações, ranking de sugestões e 4 modalidades de completion. | `reasoning` | `balanced` |
| **ONNX & Local AI** | [`onnx-ai-agent.md`](onnx-ai-agent.md) | Runtime neural ONNX GenAI, hardware probing (CPU, GPU, NPU), download verificado por SHA-256 e inferência 100% offline. | `reasoning` | `advanced-reasoning` |
| **QA & Testing** | [`qa-testing-agent.md`](qa-testing-agent.md) | Testes NUnit, Avalonia.Headless, fixtures independentes, testes de concorrência com CTS e matriz de validação. | `balanced` | `reasoning` |
| **Performance** | [`performance-agent.md`](performance-agent.md) | Benchmarks com BenchmarkDotNet, profiling de memória/GC, latência de renderização UI e tempo de primeiro token (TTFT). | `reasoning` | `advanced-reasoning` |
| **Documentation** | [`documentation-agent.md`](documentation-agent.md) | Manutenção de `docs/`, rastreabilidade do catálogo funcional (CON/DAT/...), sincronização do índice HTML e pt-BR. | `balanced` | `large-context` |

---

## 3. Abstração por Capacidade de Modelo

Os agentes **não dependem de nomes fixos de modelos** (como "Claude Sonnet" ou "GPT-4"). Eles declaram o nível de capacidade exigido para o perfil da tarefa.

A configuração central reside em [`capabilities.md`](capabilities.md):

```text
fast ───────► balanced ───────► reasoning ───────► advanced-reasoning
 (mecânico)     (padrão dev)       (arquitetura)      (crítico / risco)

large-context (análise global e correlação volumosa)
```

### Princípio da Menor Capacidade Suficiente
O `goal-orchestrator` sempre escolhe o menor nível de capacidade capaz de desempenhar a tarefa com precisão e segurança:
- **`fast`** (ex: GPT Luna): Tarefas de organização de arquivos, renomeações, geração mecânica de boilerplate.
- **`balanced`** (ex: Claude Sonnet, GPT Luna raciocínio): Implementação padrão de casos de uso, testes de unidade, UI Avalonia.
- **`reasoning`** (ex: GPT Sun, Claude Sonnet): Concorrência assíncrona com CTS, design de contratos, AST de autocomplete, diagnósticos complexos.
- **`advanced-reasoning`** (ex: Claude Opus, GPT Astro): Revisão crítica de segurança, auditoria de invariantes, persistência LiteDB sensível.
- **`large-context`** (ex: GPT Astro, Claude Opus): Auditoria de conformidade global da solução contra a documentação.

### Escalonamento de Modelo
Se um agente não conseguir concluir a tarefa com a capacidade designada (erros persistentes, incompreensão de contexto ou falhas de compilação/teste), o orquestrador aciona o escalonamento progressivo:
$$\text{fast} \xrightarrow{\text{falha}} \text{balanced} \xrightarrow{\text{falha}} \text{reasoning} \xrightarrow{\text{falha}} \text{advanced-reasoning}$$

---

## 4. Contrato Padronizado de Agente

Todos os arquivos em `/agents/` seguem rigorosamente a estrutura obrigatória de 13 seções:

```markdown
# [Nome do Agente]
## Name
## Purpose
## Responsibilities
## Inputs
## Outputs
## Allowed Actions
## Restrictions
## Preferred Model Capability
## Alternative Model Capability
## Example Models
## When to Use
## When Not to Use
## Dependencies
## Validation Rules
```

---

## 5. Protocolo de Comunicação entre Agentes

### Despacho de Tarefa (Orquestrador ➔ Especialista)
```text
Task: [ID e Título]
Context: [Contexto arquitetural e situacional]
Objective: [Objetivo exato a ser alcançado]
Constraints: [Invariantes e restrições mandatórias]
Relevant Files: [Caminhos dos arquivos relevantes]
Expected Output: [Formato e conteúdo da entrega]
Acceptance Criteria: [Critérios mensuráveis de aceite]
Preferred Capability: [fast | balanced | reasoning | advanced-reasoning | large-context]
Suggested Models: [Modelos recomendados]
```

### Retorno da Tarefa (Especialista ➔ Orquestrador)
```text
Status: [SUCCESS | PARTIAL | FAILED | BLOCKED]
Changes Performed: [Resumo das alterações realizadas]
Files Changed: [Lista de arquivos modificados ou criados]
Findings: [Descobertas técnicas relevantes]
Problems: [Problemas encontrados ou limitações]
Pending Items: [Itens pendentes]
Validation Performed: [Comandos executados e evidências]
Recommended Next Step: [Sugestão de próximo passo]
```

---

## 6. Acompanhamento e Controle de Progresso de Metas

O `goal-orchestrator` registra o progresso da meta com a seguinte estrutura:

```text
Goal Status: [Título da Meta]
[✓] Interpretação e contexto
[✓] Planejamento e decomposição
[~] Execução de tarefas
[ ] Testes e validação integrada
[ ] Conclusão e aceite final

Completed Tasks:
- [ID] Descrição (Agente, Capability)

Current Tasks:
- [ID] Descrição (Agente, Capability)

Pending Tasks:
- [ID] Descrição

Blocked Tasks:
- [ID] Descrição (Motivo do bloqueio)

Discovered Tasks:
- [ID] Descrição (Descoberta durante tarefa X)

Failed Tasks:
- [ID] Descrição (Motivo da falha)

Tasks Requiring Escalation:
- [ID] Descrição (De: Nível A ➔ Para: Nível B)
```

---

## 7. Reprocessamento dos Agentes Anteriores

Na criação desta arquitetura central, os agentes existentes foram reavaliados e reprocessados:

1. **`.claude/agents/code-organizer.md`**:
   - *Status*: Mantido, aprimorado e padronizado em [`agents/code-organizer.md`](code-organizer.md).
   - *Ajuste*: Desacoplado do modelo fixo "sonnet", recebeu capacidades formalizadas (`fast` preferencial, `balanced` alternativo) e preservou todas as diretrizes de 1 tipo por arquivo e compilação estrita.
2. **`docs/auto-complite/agents/*` (10 agentes)**:
   - *Status*: Reprocessados e consolidados em especialistas de escopo completo da solução.
   - *Ajuste*: Os perfis generalistas de autocomplete (`architecture-agent`, `testing-agent`, `performance-agent`, `onnx-runtime-agent`) foram elevados a agentes de primeira classe da solução (`architecture-agent.md`, `qa-testing-agent.md`, `performance-agent.md`, `onnx-ai-agent.md`). As especificidades de parsing de AST, catálogo de schemas, ranking e completion tradicional/preemptivo foram unificadas no [`autocomplete-agent.md`](autocomplete-agent.md) e [`mongodb-domain-agent.md`](mongodb-domain-agent.md).
3. **`AGENTS.md` (Raiz)**:
   - *Status*: Mantido como contrato de governança global do repositório, atualizado para referenciar `/agents/` como centro operacional de especialização.
