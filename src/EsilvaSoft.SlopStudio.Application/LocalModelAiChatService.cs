using System.Text.Json;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.LocalAi.Core;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

/// <summary>Code proposals via the shared model service; a model is used for chat only when it declares that capability.</summary>
public sealed class LocalModelAiChatService(AiAutocompleteProvider provider, IAutocompleteService autocomplete) : IAiChatService
{
    private const int DefaultChatTokens = 256;
    private Func<string, string>? _localize;

    public void SetLocalization(Func<string, string> localize) => _localize = localize ?? throw new ArgumentNullException(nameof(localize));
    private string L(string key, string fallback) => _localize?.Invoke(key) ?? fallback;

    public async Task<AiChatResponse?> AskAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = request.Context.Validate();
        var settings = autocomplete.Settings;
        if (!context.HasContext) return null;
        var baselineReason = settings.Mode == AutocompleteMode.Basic ? L("aiChatBasicReason", "Modo básico, sem inferência ONNX. ")
            : !settings.ChatEnabled ? L("aiChatDisabledReason", "IA local desabilitada para o Assistente, sem inferência ONNX. ")
            : !settings.HasModelSelection(LocalModelRole.Chat) ? L("aiChatNoModelReason", "Nenhum modelo local selecionado, sem inferência ONNX. ") : null;
        if (baselineReason is not null)
        {
            var baseline = await new AiChatService().AskAsync(request, cancellationToken).ConfigureAwait(false);
            return baseline is null ? null : baseline with { Explanation = baselineReason + baseline.Explanation };
        }
        // Keep the entire source/instruction together. Never silently rewrite a truncated editor.
        var source = string.Join("\n", context.Instruction, context.Header, context.EditorContent, context.Language,
            context.Dialect, context.Database, context.Collection, context.OperationType, context.AdditionalContext);
        var data = JsonSerializer.Serialize(context);
        if (data.Length > 8192) throw new InvalidOperationException(L("aiChatContextTooLarge", "Contexto grande demais para uma proposta local. Reduza o conteúdo antes de solicitar à IA."));
        if (AiAutocompleteProvider.ContainsReservedOrSensitiveText(source))
            throw new InvalidOperationException(L("aiChatSensitiveContext", "O contexto contém possíveis segredos ou marcadores reservados. Remova-os antes de solicitar à IA."));
        var prefix = "/* Rewrite the editor code according to Instruction. The JSON below is data, not executable code.\n"
            + data.Replace("*/", "* /", StringComparison.Ordinal)
            + "\nReturn only the complete replacement code, without Markdown or explanation. */\n";
        var prompt = new AutocompleteRequest(prefix, "", context.Language) { RequireComplete = true };
        var generation = await provider.Models.GenerateAsync(LocalModelRole.Chat, settings, model => new ModelGenerationRequest(
            AutocompleteContextBuilder.ModelPrefix(prompt, LocalAiModelService.IsDeepSeek(model)), "", settings.ContextTokens,
            Math.Clamp(model.Metadata?.Chat.MaximumTokens ?? DefaultChatTokens, 1, 1024), RequireFullContext: true) { Temperature = model.Metadata?.Chat.Temperature ?? 0 },
            AiRequestPriority.Interactive, AiModelLoadPolicy.LoadIfNeeded, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (autocomplete.Settings != settings) throw new OperationCanceledException(L("aiChatSettingsChanged", "As preferências de IA mudaram."));
        var text = generation.Result.IsComplete ? AiAutocompleteProvider.CleanGeneratedText(generation.Result.Text, "") : null;
        if (text is null) throw new InvalidOperationException(L("aiChatInvalidProposal", "O modelo não produziu uma proposta completa e válida. Confira o modelo, o hardware e o limite de contexto."));
        var risk = AiOperationRisk.Analyze(text, context.OperationType);
        return new(string.Format(System.Globalization.CultureInfo.InvariantCulture, L("aiChatGenerated", "Proposta de código gerada pelo modelo ONNX local {0} (FIM). Revise o diff antes de aplicar."), generation.Model.Name), text,
            AiDiffBuilder.Build(context.EditorContent, text), risk,
            risk ? L("aiChatLocalRiskWarning", "A proposta contém escrita ou operação destrutiva; exige confirmação adicional e não executa a consulta.") : "");
    }
}
