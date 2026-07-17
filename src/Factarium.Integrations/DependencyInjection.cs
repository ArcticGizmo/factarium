using Factarium.Application.Sync;
using Factarium.Integrations.ClaudeCode;
using Factarium.Integrations.GitHub;
using Factarium.Integrations.Jira;
using Microsoft.Extensions.DependencyInjection;

namespace Factarium.Integrations;

public static class DependencyInjection
{
    public static IServiceCollection AddFactariumIntegrations(this IServiceCollection services)
    {
        services.AddHttpClient<GitHubApiClient>(client =>
            client.BaseAddress = new Uri("https://api.github.com/"));
        services.AddScoped<IPullSource, GitHubPullSource>();

        // Jira base URL is per-integration, so no fixed BaseAddress.
        services.AddHttpClient<JiraApiClient>();
        services.AddScoped<IPullSource, JiraPullSource>();

        // Push source: Claude Code OTEL (no HTTP client; receives inbound payloads).
        services.AddScoped<IPushSource, ClaudeOtelPushSource>();

        return services;
    }
}
