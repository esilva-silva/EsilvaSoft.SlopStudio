using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.Anthropic;
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

    /// <summary>
    /// Registers the Claude API adapter as an <see cref="IAgentProvider"/>. Registration opens no connection and reads
    /// no secret: the provider only reports itself available, and only creates sessions, when an API Key exists in the
    /// vault slot. Authentication is API Key only, resolved by <see cref="IAgentCredentialProvider"/> from the OS
    /// vault at the start of each turn; it never imports a Claude Desktop/Code session or offers a subscription login.
    /// The shared <see cref="IAgentToolRegistry"/> is used when composed (broker opt-in); without it the provider
    /// declares no tool calling. The registered instance owns its HTTP handler and is disposed with the container.
    /// </summary>
    public static IServiceCollection AddSlopStudioClaudeAgentProvider(
        this IServiceCollection services, ClaudeAgentProviderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(ClaudeAgentProvider)))
        {
            throw new InvalidOperationException("O provider Claude já foi composto.");
        }

        var configured = options ?? new ClaudeAgentProviderOptions();
        configured.Validate();
        services.AddSingleton(provider => new ClaudeAgentProvider(
            provider.GetRequiredService<IAgentCredentialProvider>(), configured, provider.GetService<IAgentToolRegistry>()));
        services.AddSingleton<IAgentProvider>(provider => provider.GetRequiredService<ClaudeAgentProvider>());
        return services;
    }
}
