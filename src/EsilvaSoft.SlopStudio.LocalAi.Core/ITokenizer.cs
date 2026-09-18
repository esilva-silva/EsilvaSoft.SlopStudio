namespace EsilvaSoft.SlopStudio.LocalAi.Core;

public interface ITokenizer
{
    IReadOnlyList<int> Encode(string text);
    string Decode(IEnumerable<int> tokens);
}
