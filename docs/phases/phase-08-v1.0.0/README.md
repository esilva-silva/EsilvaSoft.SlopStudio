# Fase 8 — v1.0.0: estabilidade, revisão completa, instalação e atualizações

**Situação:** Planejada.

## Objetivo

Release estável para uso diário, publicada **somente após** revisão de código, arquitetura, segurança, testes, instalação e atualização.

## Escopo incluído (IDs do catálogo)

- UX-03 — instalação, atualização e recuperação de sessão.
- Revisão transversal: estabilidade, performance, UX, correção de defeitos, testes, documentação, segurança e compatibilidade Windows/Linux.

## Fora de escopo

Grande pacote de funções novas, cobertura de todos os comandos/topologias MongoDB e conclusão compulsória do backlog especializado.

## Antecipações técnicas presentes no código

Existem workflows de CI/release e `GitHubAppUpdateService` com seletor de atualização (ADR-038), verificados com pacotes sintéticos. **Workflow e tag não comprovam instalação limpa nem atualização real.**

## Critério de aceite

Nenhum defeito crítico de integridade ou de segredos aberto; suíte e regressões aprovadas; desempenho medido em datasets definidos; recuperação sem sobrescrever sessão ilegível; instalação e atualização em máquinas limpas Windows e Linux com integridade dos artefatos; teclado, leitor de tela e diálogos nativos homologados; licenças, avisos, SBOM e documentação revisados. Publicar somente compatibilidade comprovada, notas de release e limites conhecidos.

## Dependências

Aceites de todas as fases anteriores, laboratórios de teste, distribuição e credenciais de assinatura quando necessárias.

## Documentos relacionados

- [Testes e qualidade](../../08-testes-e-qualidade.md) · [Compatibilidade](../../04-compatibilidade-e-capacidades.md) · [Matriz de validação](../../15-matriz-de-validacao.md) · [Checklist de homologação](../../16-checklist-homologacao.md) · [Avisos de terceiros](../../../THIRD-PARTY-NOTICES.md)

## Pendências de homologação real

Instalação e atualização em máquina limpa, acessibilidade com leitor de tela, diálogos nativos e Linux gráfico continuam sem evidência.
