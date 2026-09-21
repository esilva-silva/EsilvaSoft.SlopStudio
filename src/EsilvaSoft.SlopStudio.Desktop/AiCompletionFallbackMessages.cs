using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.LocalAi.Core;

namespace EsilvaSoft.SlopStudio.Desktop;

/// <summary>
/// Traduz a recusa tipada da IA explícita na linha de estado que acompanha a lista tradicional.
/// </summary>
/// <remarks>
/// <para><strong>A mensagem deriva do motivo, nunca o contrário</strong> ([DEC-R41-REASONS]). Nada aqui inspeciona o
/// texto de <see cref="AiCompletionUpdate.Message"/> para decidir: o texto do serviço só é usado quando não há motivo
/// tipado algum, caso em que ele é a única informação existente.</para>
/// <para><strong>Uma linha, nenhum diálogo.</strong> É o que a seção de riscos da Fase 4 pede; o destino desta
/// string é o rodapé da própria lista tradicional, junto dos atalhos dela.</para>
/// </remarks>
public static class AiCompletionFallbackMessages
{
    /// <summary>A IA explícita está desligada nas preferências (modo Básico ou autocomplete desabilitado).</summary>
    public const string Disabled = "IA desabilitada nas preferências.";

    /// <summary>Esta aba não recebeu provider de IA explícita (aba isolada de projeto ou de teste).</summary>
    public const string NotConfigured = "IA explícita não está disponível nesta aba.";

    /// <summary>Falha inesperada ao consumir o fluxo de geração; a mensagem nunca carrega erro nativo cru.</summary>
    public const string Failed = "A IA local não pôde atender a este pedido.";

    /// <summary>
    /// A lista tradicional explícita está desligada nas preferências e por isso não foi aberta. O fallback informa a
    /// indisponibilidade e nada mais: não reabre uma apresentação desligada nem altera a preferência para mostrá-la.
    /// </summary>
    public const string TraditionalDisabled = "Lista tradicional desligada nas preferências.";

    /// <summary>Linha de estado para uma atualização final que não trouxe candidato.</summary>
    /// <param name="update">Atualização final recusada.</param>
    /// <param name="now">Relógio de quem exibe, para o tempo restante de uma janela de recusa.</param>
    public static string Describe(AiCompletionUpdate update, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(update);
        var text = update.Failure switch
        {
            AiCompletionFailure.Privacy => "Contexto contém possível segredo; a IA local não foi consultada.",
            AiCompletionFailure.BudgetExhausted => "O contexto mínimo não cabe na janela do modelo selecionado.",
            AiCompletionFailure.Rejected => "A IA local não produziu uma sugestão utilizável.",
            // Prazo vencido sem nada aproveitável. Com prévia parcial válida o pedido nem chega aqui: ele termina em
            // sucesso parcial e a prévia fica na tela.
            AiCompletionFailure.Timeout => "A IA local excedeu o tempo limite sem produzir uma sugestão.",
            // Descarte silencioso; existe para completude do mapeamento, e quem chama não exibe esta linha.
            AiCompletionFailure.Preempted => "Geração descartada por uma ação de prioridade maior.",
            AiCompletionFailure.ModelUnavailable => Describe(update.Reason, update.Message),
            _ => Failed
        };
        if (update.RetryAfter is not { } retry || retry <= now) return text;
        var seconds = Math.Max(1, (int)Math.Ceiling((retry - now).TotalSeconds));
        return $"{text} Nova tentativa em {seconds} s.";
    }

    private static string Describe(LocalModelUnavailableReason? reason, string message) => reason switch
    {
        LocalModelUnavailableReason.NoModelConfigured => "Nenhum modelo de IA selecionado; escolha um em Preferências.",
        LocalModelUnavailableReason.ModelInvalid => "O pacote de modelo selecionado não pode ser usado por esta versão.",
        LocalModelUnavailableReason.CapabilityMissing => "O modelo selecionado não declara a capacidade de autocomplete.",
        LocalModelUnavailableReason.ProviderUnavailable => "O acelerador exigido pelo modelo não está disponível nesta máquina.",
        LocalModelUnavailableReason.Cooldown => "IA local indisponível após uma falha recente.",
        LocalModelUnavailableReason.NotLoaded => "Nenhum modelo de IA carregado.",
        LocalModelUnavailableReason.DifferentConfiguration => "Outro modelo está carregado e este pedido não pode trocá-lo.",
        LocalModelUnavailableReason.ContextOverflow => "O pedido não cabe na janela do modelo selecionado.",
        LocalModelUnavailableReason.RuntimeFailure => "Falha do runtime de IA local; confira o status do modelo.",
        // Sem motivo tipado, a mensagem do serviço é a única informação que existe — e ela já vem segura da camada
        // de aplicação (sem texto do editor e sem erro nativo cru).
        _ => message.Length > 0 ? message : Failed
    };
}
