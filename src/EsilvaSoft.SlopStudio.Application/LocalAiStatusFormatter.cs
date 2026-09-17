using System.Globalization;
using EsilvaSoft.SlopStudio.LocalAi.Core;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

/// <summary>pt-BR text for model state, detected hardware and model tests. Metrics that could not be measured are omitted.</summary>
public static class LocalAiStatusFormatter
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public static string HardwareLabel(AiAccelerationMode mode) => mode switch
    {
        AiAccelerationMode.Cpu => "CPU",
        AiAccelerationMode.Gpu => "GPU",
        AiAccelerationMode.Npu => "NPU",
        _ => "Automático"
    };

    public static string StateLabel(LocalModelState state) => state switch
    {
        LocalModelState.NotInstalled => "Não instalado",
        LocalModelState.NotLoaded => "Não carregado",
        LocalModelState.Available => "Arquivos encontrados",
        LocalModelState.Loading => "Carregando",
        LocalModelState.Ready => "Carregado",
        LocalModelState.Invalid => "Inválido",
        LocalModelState.Unsupported => "Não suportado",
        LocalModelState.MissingFiles => "Arquivos ausentes",
        _ => "Falha"
    };

    public static string ValidityLabel(LocalModelValidity validity) => validity switch
    {
        LocalModelValidity.Valid => "válido",
        LocalModelValidity.Unsupported => "não suportado",
        LocalModelValidity.MissingFiles => "arquivos ausentes",
        _ => "inválido"
    };

    /// <summary>Automatic considers NPU, GPU and CPU in this order among reported devices; model compatibility is checked at load.</summary>
    public static AiHardwareDevice? PredictDevice(AiAccelerationMode requested, IReadOnlyList<AiHardwareDevice> hardware)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        AiAccelerationMode[] order = requested == AiAccelerationMode.Auto
            ? [AiAccelerationMode.Npu, AiAccelerationMode.Gpu, AiAccelerationMode.Cpu] : [requested];
        foreach (var kind in order)
            if (hardware.FirstOrDefault(device => device.Kind == kind && device.IsAvailable) is { } device) return device;
        return null;
    }

    public static string DeviceLine(AiHardwareDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!device.IsAvailable) return HardwareLabel(device.Kind) + " — indisponível";
        var details = new[] { device.Kind == AiAccelerationMode.Cpu ? null : device.Provider, device.MemoryBytes is { } memory ? Bytes(memory) : null }
            .Where(value => !string.IsNullOrEmpty(value)).ToArray();
        return $"{HardwareLabel(device.Kind)} — {device.Name}" + (details.Length == 0 ? "" : $" ({string.Join(", ", details)})");
    }

    public static string Format(LocalModelStatus status, string? selectedModel, AiAccelerationMode requested, IReadOnlyList<AiHardwareDevice> hardware)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(hardware);
        var lines = new List<string>
        {
            "Modelo selecionado: " + (string.IsNullOrWhiteSpace(selectedModel) ? "nenhum" : selectedModel),
            "Estado: " + StateLabel(status.State),
            "Hardware: " + HardwareLabel(requested)
        };
        // Preferences may show a new, unsaved selection while another model is still loaded.
        if (status.State is LocalModelState.Ready or LocalModelState.Loading && !string.IsNullOrEmpty(status.ModelName) && status.ModelName != selectedModel)
            lines.Insert(1, "Modelo em uso: " + status.ModelName);
        if (status.Backend is { } backend)
        {
            lines.Add("Backend: " + HardwareLabel(backend) + (status.UsedFallback ? " (aceleração preferida indisponível)" : ""));
            if (!string.IsNullOrEmpty(status.Provider)) lines.Add("Provider: " + status.Provider);
            if (!string.IsNullOrEmpty(status.Device)) lines.Add("Dispositivo: " + status.Device);
            if (status.LoadTime is { } load) lines.Add("Tempo de carregamento: " + Milliseconds(load));
            if (status.ProcessMemoryBytes is { } memory) lines.Add("Memória do processo: " + Bytes(memory));
            if (status.FirstToken is { } first) lines.Add("Primeiro token: " + Milliseconds(first));
            if (status.TokensPerSecond is { } rate) lines.Add("Geração: " + Rate(rate));
        }
        else if (hardware.Count > 0)
        {
            var predicted = PredictDevice(requested, hardware);
            lines.Add("Provider detectado: " + (predicted?.Provider ?? "indisponível"));
            if (predicted is not null) lines.Add("Dispositivo: " + predicted.Name);
        }
        if (!string.IsNullOrWhiteSpace(status.Message)) lines.Add(status.Message);
        return string.Join('\n', lines);
    }

    public static string FormatReport(LocalModelTestReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var lines = new List<string> { report.Message };
        if (!string.IsNullOrEmpty(report.ModelName)) lines.Add("Modelo: " + report.ModelName);
        if (report.Backend is { } backend)
            lines.Add("Hardware: " + HardwareLabel(backend) + (report.Hardware == AiAccelerationMode.Auto ? " (Automático)" : "")
                + (report.UsedFallback ? " — aceleração preferida indisponível" : ""));
        else if (report.Hardware is { } hardware) lines.Add("Hardware solicitado: " + HardwareLabel(hardware));
        if (!string.IsNullOrEmpty(report.Provider)) lines.Add("Provider: " + report.Provider);
        if (!string.IsNullOrEmpty(report.Device)) lines.Add("Dispositivo: " + report.Device);
        if (report.LoadTime is { } load) lines.Add("Carregamento: " + Milliseconds(load));
        if (report.FirstToken is { } first) lines.Add("Primeiro token: " + Milliseconds(first));
        if (report.TokensPerSecond is { } rate) lines.Add("Geração: " + Rate(rate));
        if (report.Steps.Count > 0)
            lines.Add("Etapas: " + string.Join(" · ", report.Steps.Select(step => $"{step.Name}: {(step.Succeeded ? "OK" : "falhou")}")));
        return string.Join('\n', lines);
    }

    private static string Milliseconds(TimeSpan value) => value.TotalMilliseconds.ToString("N0", Culture) + " ms";
    private static string Rate(double value) => value.ToString(value < 10 ? "N1" : "N0", Culture) + " tokens/s";
    private static string Bytes(long value) => value >= 1L << 30
        ? (value / (double)(1L << 30)).ToString("N1", Culture) + " GB"
        : (value / (double)(1L << 20)).ToString("N0", Culture) + " MB";
}
