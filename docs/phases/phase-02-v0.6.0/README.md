# Fase 2 — v0.6.0: organização dos projetos e autocomplete básico

**Situação:** **Em execução.** É a fase ativa; o recorte técnico possui implementação e evidência automatizada.

A meta transversal de internacionalização que atravessa o autocomplete foi concluída no recorte automatizado; esta fase continua em execução apenas pelo seu escopo próprio de separação de núcleos e autocomplete determinístico.

## Objetivo

Separar os núcleos da solução, consolidar as regras de negócio nas camadas corretas e entregar o autocomplete determinístico simples.

## Escopo incluído (IDs do catálogo)

- Separação de núcleos: `EsilvaSoft.SlopStudio.Autocomplete.Core` e `EsilvaSoft.SlopStudio.LocalAi.Core` isolados de `Core`, `Application`, `Infrastructure` e `Desktop` (ver [ADR-040](../../10-decisoes-arquiteturais.md)).
- EDT-02 — autocomplete determinístico: comandos, operadores, nomes conhecidos, campos e contexto/histórico local, sem pesos de IA e sem consultar o servidor a cada tecla.
- Regras de negócio posicionadas na camada correta, sem acesso a MongoDB, LiteDB ou provider de IA a partir de `Core`.

## Fora de escopo

Sugestões assistidas por modelo (Fase 3), ghost text preemptivo (Fase 5), administração (Fase 6) e qualquer runtime novo de automação.

## Antecipações técnicas presentes no código

- **Agregação** e consultas avançadas: implementadas e auditadas, porém **fora do escopo desta fase**. O modo foi desativado na interface e o requisito está em [`backlog/bkl-04-modo-aggregation.md`](../../backlog/bkl-04-modo-aggregation.md). A auditoria original está preservada em [`backlog/27-consultas-avancadas.md`](../../backlog/27-consultas-avancadas.md).
- Syntax highlighting semântico e execução por statement: integrados e mantidos ativos, pois sustentam o editor da fase atual.

## Critério de aceite

Núcleos separados sem dependência circular e com build sem avisos; autocomplete determinístico funcionando sem modelo carregado, sem cruzar conexão/aba e sem I/O no caminho da tecla; testes de parser, contexto, ranking e snippets aprovados.

## Dependências

Escopo funcional da v0.5.0 ([Fase 1](../phase-01-v0.5.0/README.md)).

## Documentos relacionados

- [Autocomplete local — contrato](../../21-autocomplete-local.md)
- [Plano revisado do autocomplete](../../auto-complite/README.md) e suas sub-fases [1](../../auto-complite/phases/phase-1-data-traditional.md) e [2](../../auto-complite/phases/phase-2-traditional-autocomplete.md) — numeração própria do subsistema, não das fases do produto
- [Arquitetura](../../05-arquitetura.md) · [ADRs](../../10-decisoes-arquiteturais.md) · [Highlighting](../../22-syntax-highlighting.md)

## Validação manual transferida

O corpus automatizado e o gate de ranking estão concluídos (675 fixtures mais um gate; MRR 1,000; top-1/top-5 44/44). Desempenho p95/p99, matriz visual em plataforma real, integração de metadata com MongoDB real, Linux gráfico e acessibilidade são critérios da [Fase 8 / v0.12.0](../phase-08-v0.12.0/README.md).
