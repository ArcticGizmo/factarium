using System.Text;
using Factarium.Integrations.ClaudeCode;

namespace Factarium.Tests.Integrations;

public class OtlpMetricParserTests
{
    private const string Payload = """
        {
          "resourceMetrics": [{
            "resource": { "attributes": [
              {"key":"service.name","value":{"stringValue":"claude-code"}},
              {"key":"user.email","value":{"stringValue":"dev@example.com"}},
              {"key":"session.id","value":{"stringValue":"sess-1"}}
            ]},
            "scopeMetrics": [{
              "metrics": [
                {"name":"claude_code.cost.usage","unit":"USD","sum":{"dataPoints":[
                  {"attributes":[{"key":"model","value":{"stringValue":"claude-opus"}}],
                   "timeUnixNano":"1700000000000000000","asDouble":0.42}
                ]}},
                {"name":"claude_code.token.usage","sum":{"dataPoints":[
                  {"attributes":[{"key":"type","value":{"stringValue":"input"}}],
                   "timeUnixNano":"1700000000000000000","asInt":"1500"}
                ]}}
              ]
            }]
          }]
        }
        """;

    [Fact]
    public void Flattens_points_merges_attributes_and_reads_values()
    {
        var points = OtlpMetricParser.Parse(Encoding.UTF8.GetBytes(Payload));

        Assert.Equal(2, points.Count);

        var cost = points.Single(p => p.Name == "claude_code.cost.usage");
        Assert.Equal(0.42, cost.Value, 3);
        Assert.Equal("USD", cost.Unit);
        Assert.Equal("dev@example.com", cost.Attributes["user.email"]); // resource-level merged in
        Assert.Equal("sess-1", cost.Attributes["session.id"]);
        Assert.Equal("claude-opus", cost.Attributes["model"]);          // data-point-level

        var tokens = points.Single(p => p.Name == "claude_code.token.usage");
        Assert.Equal(1500, tokens.Value, 3);   // asInt string parsed
        Assert.Equal("input", tokens.Attributes["type"]);
    }

    [Fact]
    public void Returns_empty_for_payload_without_metrics()
    {
        var points = OtlpMetricParser.Parse("{}"u8);
        Assert.Empty(points);
    }
}
