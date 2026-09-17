namespace EsilvaSoft.SlopStudio.Application;

public static class AiDiffBuilder
{
    public static string Build(string original, string proposed)
    {
        if (string.Equals(original, proposed, StringComparison.Ordinal)) return "Sem alterações.";
        var oldLines = original.Replace("\r\n", "\n").Split('\n');
        var newLines = proposed.Replace("\r\n", "\n").Split('\n');
        var common = 0;
        while (common < oldLines.Length && common < newLines.Length && oldLines[common] == newLines[common]) common++;
        var oldTail = oldLines.Length - 1;
        var newTail = newLines.Length - 1;
        while (oldTail >= common && newTail >= common && oldLines[oldTail] == newLines[newTail]) { oldTail--; newTail--; }
        var lines = new List<string> { "@@ proposta revisável @@" };
        for (var i = common; i <= oldTail; i++) lines.Add("- " + oldLines[i]);
        for (var i = common; i <= newTail; i++) lines.Add("+ " + newLines[i]);
        return string.Join(Environment.NewLine, lines);
    }
}
