using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.OpenAi;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents;

public static class AgentProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers the OpenAI API adapter as an <see cref="IAgentProvider"/>. Registration opens no connection and reads
    /// no secret: the provider only reports itself available, and only creates sessions, when an API Key exists in the
    /// vault slot. The shared <see cref="IAgentToolRegistry"/> is used when composed (broker opt-in); without it the
    /// provider declares no tool calling. Requires <see cref="IAgentCredentialProvider"/>.
    /// </summary>
    public static IServiceCollection AddSlopStudioOpenAiAgentProvider(
        this IServiceCollection services, OpenAiAgentProviderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(OpenAiAgentProvider)))
        {
            throw new InvalidOperationException("O provider OpenAI já foi composto.");
        }

        var configured = options ?? new OpenAiAgentProviderOptions();
        configured.Validate();
        services.AddSingleton(provider => new OpenAiAgentProvider(
            provider.GetRequiredService<IAgentCredentialProvider>(), configured, provider.GetService<IAgentToolRegistry>()));
        services.AddSingleton<IAgentProvider>(provider => provider.GetRequiredService<OpenAiAgentProvider>());
        return services;
    }
}
