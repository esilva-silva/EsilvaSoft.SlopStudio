using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure.LocalAi;

/// <summary>An execution provider configurable through ONNX Runtime GenAI.</summary>
public sealed record OnnxExecutionProviderDescriptor(string OrtName, string DisplayName, string GenAiName, AiAccelerationMode Kind, AiExecutionProvider? Setting);
