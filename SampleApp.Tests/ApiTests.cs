using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SampleApp.Tests;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsOkStatusCode()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsHealthyStatusInBody()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        var status = json.RootElement.GetProperty("status").GetString();

        Assert.Equal("Healthy", status);
    }

    [Fact]
    public async Task WeatherForecast_ReturnsOkStatusCode()
    {
        var response = await _client.GetAsync("/weatherforecast");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WeatherForecast_ReturnsExactlyFiveItems()
    {
        var response = await _client.GetAsync("/weatherforecast");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        var itemCount = json.RootElement.GetArrayLength();

        Assert.Equal(5, itemCount);
    }

    [Fact]
    public async Task WeatherForecast_TemperatureFIsCorrectlyCalculatedFromCelsius()
    {
        var response = await _client.GetAsync("/weatherforecast");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        foreach (var item in json.RootElement.EnumerateArray())
        {
            var celsius = item.GetProperty("temperatureC").GetInt32();
            var fahrenheit = item.GetProperty("temperatureF").GetInt32();
            var expectedFahrenheit = 32 + (int)(celsius / 0.5556);

            Assert.Equal(expectedFahrenheit, fahrenheit);
        }
    }
}
