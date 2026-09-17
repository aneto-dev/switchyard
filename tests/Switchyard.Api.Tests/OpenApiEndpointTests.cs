using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Switchyard.Api.Tests;

public sealed class OpenApiEndpointTests
{
    [Fact]
    public async Task OpenApiDocumentIsAvailableInDevelopment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }
}
