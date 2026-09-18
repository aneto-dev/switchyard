using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Switchyard.Api.Tests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task LiveEndpointReturnsSafeHealthyResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/health/live", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>(cancellationToken);

        Assert.NotNull(payload);
        Assert.Equal("healthy", payload.Status);

        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("connection", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", rawBody, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record HealthResponse(string Status);
}
