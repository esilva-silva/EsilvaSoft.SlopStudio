using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.LocalAi.Core;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.SlopStudio.Infrastructure.LocalAi;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSlopStudioLocalAiInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ILocalModelCatalog>(_ => new LocalModelCatalog());
        services.AddSingleton<IRemoteModelSource>(_ => new HuggingFaceModelSource());
        services.AddSingleton<IAiHardwareProbe, OnnxHardwareProbe>();
        // One model service shared by autocomplete, chat and the preferences window.
        services.AddSingleton<ILocalAiModelService>(provider => new LocalAiModelService(
            provider.GetRequiredService<ILocalModelCatalog>(),
            () => new OnnxLocalModelRuntime(provider.GetRequiredService<IAutocompleteDiagnostics>(), provider.GetRequiredService<IAiHardwareProbe>()),
            provider.GetRequiredService<IAiHardwareProbe>(),
            provider.GetRequiredService<IAutocompleteDiagnostics>(),
            provider.GetRequiredService<IApplicationOperationService>()));
        return services;
    }
}
