# Dependências do Console

EsilvaSoft.SlopStudio mantém sua licença MIT. Estas dependências têm licenças próprias, conforme os manifests NuGet e os projetos de origem:

| Dependência | Versão | Licença | Autoria / origem |
| --- | --- | --- | --- |
| Jint | 4.16.0 | BSD-2-Clause | Sébastien Ros — https://github.com/sebastienros/jint |
| Acornima | 1.7.0 | BSD-3-Clause | Adam Simon — https://github.com/adams85/acornima |

Jint é o interpretador JavaScript; Acornima é seu parser transitivo. Os lockfiles fixam versões e hashes NuGet. A fixture MongoDB Community portátil fica apenas em `.cache` para homologação e não integra a distribuição da aplicação.

Esta relação documenta o incremento Console; o inventário completo do produto continua incluindo as dependências já declaradas em Directory.Packages.props e seus manifests/licenças.

## Autocomplete local

Microsoft.ML.OnnxRuntimeGenAI/Managed **0.15.2** e Microsoft.ML.OnnxRuntime/Managed **1.28.0**, Microsoft, licença MIT. [ONNX Runtime GenAI](https://github.com/microsoft/onnxruntime-genai/tree/v0.15.2), [ONNX Runtime](https://github.com/microsoft/onnxruntime). Os pacotes incluem avisos de componentes nativos transitivos que devem acompanhar distribuições derivadas. Os lockfiles fixam versões e hashes.

Pesos e tokenizers Qwen não fazem parte do repositório nem da publicação. A licença de cada modelo/exportação deve ser consultada na origem escolhida pelo usuário; a MIT do Slop Studio não relicencia modelos externos.

## Planejamento v0.11.0 — candidatos externos

A meta de planejamento MCP/providers não adiciona dependências nem redistribui SDKs ou runtimes de OpenAI, Anthropic ou MCP. A pesquisa, as distinções entre licença de código e termos de serviço e os gates de versão/transitivas estão em [Fontes e licenças da fase 7](docs/phases/phase-07-v0.11.0/15-fontes-e-licencas.md). Candidatos não devem ser confundidos com componentes já incorporados; os avisos definitivos serão atualizados ao selecionar artefatos concretos.
