using EsilvaSoft.SlopStudio.LocalAi.Core;
using EsilvaSoft.SlopStudio.Application;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace EsilvaSoft.SlopStudio.Infrastructure.LocalAi;

internal sealed class OnnxModelTokenizer(Model model) : ITokenizer, IDisposable
{
    private readonly Tokenizer _native = new(model);
    public IReadOnlyList<int> Encode(string text) { using var sequences = _native.Encode(text); return sequences[0].ToArray(); }
    public string Decode(IEnumerable<int> tokens) => _native.Decode(tokens.ToArray());
    public void Dispose() => _native.Dispose();
}
