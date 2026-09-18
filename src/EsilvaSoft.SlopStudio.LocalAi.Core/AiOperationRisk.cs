using System.Text.RegularExpressions;

namespace EsilvaSoft.SlopStudio.LocalAi.Core;

public static class AiOperationRisk
{
    public static bool Analyze(string code, string operationType)
    {
        if (!string.Equals(operationType, "consulta", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(operationType, "script", StringComparison.OrdinalIgnoreCase)) return true;
        return Regex.IsMatch(code, @"\.(?:insert|insertOne|insertMany|update|updateOne|updateMany|replaceOne|delete|deleteOne|deleteMany|drop|dropDatabase|bulkWrite|createIndex)\s*\(|\$(?:out|merge)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
