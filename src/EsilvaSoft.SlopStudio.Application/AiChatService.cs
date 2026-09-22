using System.Text.RegularExpressions;
using EsilvaSoft.SlopStudio.LocalAi.Core;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

/// <summary>
/// Local, safe baseline for the chat contract. Providers with a conversational model can replace this service in DI.
/// The deterministic query transformation also keeps the feature useful when no external model is installed.
/// </summary>
public sealed class AiChatService : IAiChatService
{
    private static readonly Regex LimitInstruction = new(@"\b(?:limite|limitar|limit)(?:\s+\p{L}+){0,4}\s+(?:a\s+)?(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private Func<string, string>? _localize;

    public void SetLocalization(Func<string, string> localize) => _localize = localize ?? throw new ArgumentNullException(nameof(localize));
    private string L(string key, string fallback) => _localize?.Invoke(key) ?? fallback;

    public Task<AiChatResponse?> AskAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var context = request.Context.Validate();
        if (!context.HasContext) return Task.FromResult<AiChatResponse?>(null);

        var proposed = context.EditorContent;
        var instruction = context.Instruction;
        var days = Regex.Match(instruction, @"\b(\d+)\s+dias\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var limit = LimitInstruction.Match(instruction);
        var changed = false;

        // This is intentionally conservative: only add a known filter to a find call with an object literal.
        // Ambiguous code remains untouched and is still shown to the user as a reviewable no-op proposal.
        if (days.Success && proposed.Contains(".find({", StringComparison.Ordinal))
        {
            var closing = proposed.IndexOf("})", proposed.IndexOf(".find({", StringComparison.Ordinal) + 6, StringComparison.Ordinal);
            if (closing >= 0 && !proposed.Contains("criadoEm", StringComparison.OrdinalIgnoreCase))
            {
                var bodyStart = proposed.IndexOf(".find({", StringComparison.Ordinal) + ".find({".Length;
                var body = proposed[bodyStart..closing].Trim();
                var daysValue = days.Groups[1].Value;
                var existingFilter = string.IsNullOrWhiteSpace(body) ? "" : "  " + body + ",\n";
                proposed = proposed[..bodyStart] + "\n" + existingFilter + "  "
                    + $"criadoEm: {{\n    $gte: new Date(Date.now() - {daysValue} * 24 * 60 * 60 * 1000)\n  }}\n"
                    + proposed[closing..];
                changed = true;
            }
        }

        if (limit.Success && !Regex.IsMatch(proposed, @"\.limit\s*\(", RegexOptions.CultureInvariant))
        {
            proposed = proposed.TrimEnd() + $".limit({limit.Groups[1].Value})";
            changed = true;
        }

        var risk = AiOperationRisk.Analyze(proposed, context.OperationType);
        var explanation = changed
            ? L("aiChatChanged", "Analisei o editor e o contexto MongoDB. A proposta adiciona apenas os filtros e o limite solicitados; revise o código antes de aplicar.")
            : L("aiChatUnchanged", "Analisei a instrução e o conteúdo atual. Não encontrei uma transformação segura e inequívoca; a proposta mantém o editor para sua revisão.");
        var warning = risk ? L("aiChatRiskWarning", "A proposta contém uma operação de escrita ou destrutiva. A aplicação exigirá uma confirmação adicional e não executará a consulta.") : "";
        var response = new AiChatResponse(explanation, proposed, AiDiffBuilder.Build(context.EditorContent, proposed), risk, warning);
        return Task.FromResult<AiChatResponse?>(response);
    }
}
