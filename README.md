# EsilvaSoft.SlopStudio

IDE desktop MongoDB em **.NET 10/Avalonia**, para **Windows e Linux**, com persistência local **LiteDB** e licença **MIT**. Interface e documentação em pt-BR; código e identificadores em inglês.

**Estado atual:** desenvolvimento após a tag alpha local `v0.1.1-alpha`; publicação remota não verificada nesta revisão. **Próxima versão alvo: v0.5.0 (MVP)**, ainda não concluída. A versão dos artefatos de release vem da tag, não deste roadmap.

| Versão alvo | Entrega | Status |
| --- | --- | --- |
| v0.5.0 | Explorer, consultas simples, CRUD, autocomplete básico, formatação e exportação JSON/CSV | 🚧 Em desenvolvimento |
| v0.6.0 | Aggregation, ferramentas de query, autocomplete contextual e UX | 🚧 Em desenvolvimento; recursos antecipados |
| v0.7.0 | Administração, índices e manutenção MongoDB | 🚧 Em desenvolvimento; ferramentas existentes |
| v0.8.0 | Script Engine JavaScript e múltiplas conexões | 🚧 Em desenvolvimento; runtimes existentes |
| v0.9.0 | IA local, ghost text e chat contextual | 🧪 Experimental na assistência por modelo |
| v1.0.0 | Estabilidade, qualidade, performance, instalação e atualização | 📋 Planejado como release estável |

O [roadmap](docs/09-plano-de-implementacao.md) define escopo, exclusões, dependências e aceite por versão. O [catálogo funcional](docs/03-catalogo-funcional.md) mantém os requisitos com status; o [inventário conferido no código](docs/24-inventario-roadmap.md) mostra o que existe e o que falta. Recursos avançados antecipados continuam disponíveis; não tornam suas fases concluídas.

✅ **Já implementado no recorte básico:** perfis e Explorer sob demanda, Console com find/findOne/filtro/sort/limit/skip, CRUD protegido, resultados JSON/árvore, autocomplete determinístico, abas e cancelamento isolados, histórico/arquivos, rascunhos e temas claro/escuro. Abrir coleção prepara a consulta; o usuário executa explicitamente. BSON/Extended JSON, UUID, ObjectId e datas têm tratamento próprio.

🚧 **Lacunas do MVP:** exportação atual é somente Extended JSON da página carregada; CSV ainda está planejado. Há formatação JSON de apresentação, mas não foi identificado comando geral para formatar queries e scripts. Homologação real dos fluxos nos dois sistemas permanece um gate separado de testes Headless.

🚧 **Antecipações:** pipelines, explain nas ferramentas, índices/administração, transferência lógica e Console JavaScript com múltiplas conexões já possuem implementação. O modo Script/mongosh é separado. 🧪 **IA local opcional:** ONNX com modelos externos, ghost text e chat revisável; funciona sem modelo com sugestões determinísticas. Inferência CPU tem evidências datadas; fidelidade conversacional e GPU não estão homologadas. [Console](docs/20-console.md), [autocomplete](docs/21-autocomplete-local.md) e [limites ONNX/chat](docs/23-onnx-slopcoder.md).

Conexões aceitam credenciais diretas na URI ou referências opcionais `${ENV.get("MONGO_PASSWORD")}`. Ambientes são locais no LiteDB; cofre nativo permanece planejado. [Guia de credenciais e ambientes](docs/14-guia-de-uso.md#credenciais-diretas-e-ambientes-opcionais).

## Desenvolvimento e validação

```powershell
dotnet restore EsilvaSoft.SlopStudio.slnx --locked-mode
dotnet build EsilvaSoft.SlopStudio.slnx --no-restore
dotnet test EsilvaSoft.SlopStudio.slnx --no-build --no-restore
```

Em ambiente isolado que bloqueia telemetria de build Avalonia, usar `-p:UsedAvaloniaProducts=` no build. Consulte [AGENTS.md](AGENTS.md), [design system](docs/17-design-system-ui-ux.md), [matriz de validação](docs/15-matriz-de-validacao.md) e [pendências de homologação](docs/16-checklist-homologacao.md). A evidência histórica mais recente registra 530 testes aprovados e uma falha; esta revisão documental não reexecutou a suíte nem certifica o checkout atual.

[CI](.github/workflows/ci.yml) configura restore/build/test em Windows/Linux. [Release](.github/workflows/release.yml) usa tags de versão e runners self-hosted para gerar ZIP Windows, tar.gz Linux e SHA256SUMS, com versão extraída da tag. Workflow existente não comprova publicação, instalação ou atualização homologada.

Comece pelo [índice da documentação](docs/README.md). [Acompanhamento](docs/12-acompanhamento-da-implementacao.md) preserva o histórico. [Licença MIT](LICENSE) e [avisos de terceiros](THIRD-PARTY-NOTICES.md) permanecem aplicáveis; esta reorganização não altera dependências ou licenças.
