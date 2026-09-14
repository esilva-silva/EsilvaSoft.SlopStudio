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
