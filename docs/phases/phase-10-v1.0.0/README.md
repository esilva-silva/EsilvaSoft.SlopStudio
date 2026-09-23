# Fase 10 — v1.0.0: estabilidade, revisão completa, instalação e atualizações

**Situação:** Planejada.

## Objetivo

Release estável para uso diário, publicada **somente após** revisão de código, arquitetura, segurança, testes, instalação e atualização, e a conclusão da Fase 9.

## Escopo incluído (IDs do catálogo)

- UX-03 — instalação, atualização e recuperação de sessão.
- Revisão transversal: estabilidade, performance, UX, correção de defeitos, testes, documentação e segurança.

## Fora de escopo

Grande pacote de funções novas, cobertura de todos os comandos/topologias MongoDB e conclusão compulsória do backlog especializado.

## Antecipações técnicas presentes no código

Existem workflows de CI/release e `GitHubAppUpdateService` com seletor de atualização (ADR-038), verificados com pacotes sintéticos.

## Critério de aceite

Nenhum defeito crítico de integridade ou de segredos aberto; suíte e regressões aprovadas; desempenho medido em datasets definidos; recuperação sem sobrescrever sessão ilegível; licenças, avisos, SBOM, documentação, notas de release e limites conhecidos revisados. A validação manual é pré-requisito já concluído na Fase 9.

## Dependências

Aceites funcionais das fases anteriores, [Fase 9 / v0.13.0](../phase-09-v0.13.0/README.md), distribuição e credenciais de assinatura quando necessárias.

## Documentos relacionados

- [Testes e qualidade](../../08-testes-e-qualidade.md) · [Compatibilidade](../../04-compatibilidade-e-capacidades.md) · [Matriz de validação](../../15-matriz-de-validacao.md) · [Fase 9 — homologação manual](../phase-09-v0.13.0/README.md) · [Avisos de terceiros](../../../THIRD-PARTY-NOTICES.md)
