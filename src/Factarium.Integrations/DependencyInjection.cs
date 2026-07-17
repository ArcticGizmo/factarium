using Factarium.Application.Sync;
using Factarium.Integrations.GitHub;
using Microsoft.Extensions.DependencyInjection;

namespace Factarium.Integrations;

public static class DependencyInjection
{
    public static IServiceCollection AddFactariumIntegrations(this IServiceCollection services)
    {
        services.AddHttpClient<GitHubApiClient>(client =>
            client.BaseAddress = new Uri("https://api.github.com/"));

        services.AddScoped<IPullSource, GitHubPullSource>();

        return services;
    }
}
