using Factarium.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Factarium.Tests.Infrastructure;

public class ConnectionStringResolutionTests
{
    [Fact]
    public void Prefers_configured_connection_string()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Factarium"] = "Host=configured;Database=f;Username=u;Password=p",
            })
            .Build();

        var resolved = DependencyInjection.ResolveConnectionString(config);

        Assert.Contains("Host=configured", resolved);
    }

    [Fact]
    public void Throws_when_no_connection_string_available()
    {
        var previous = Environment.GetEnvironmentVariable("FACTARIUM_DB");
        Environment.SetEnvironmentVariable("FACTARIUM_DB", null);
        try
        {
            var config = new ConfigurationBuilder().Build();

            Assert.Throws<InvalidOperationException>(
                () => DependencyInjection.ResolveConnectionString(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FACTARIUM_DB", previous);
        }
    }
}
