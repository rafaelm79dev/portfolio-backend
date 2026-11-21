using System.Net;

namespace ApiUsers.Tests;

[Collection("api")]
public class SmokeTests(ApiFactory factory)
{
    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
