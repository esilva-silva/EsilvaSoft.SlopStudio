using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure.LocalAi;

/// <summary>An execution provider device reported by ONNX Runtime.</summary>
public sealed record OnnxEpDevice(string ExecutionProvider, AiAccelerationMode Kind, string? Description, long? MemoryBytes = null, int? AdapterIndex = null);
