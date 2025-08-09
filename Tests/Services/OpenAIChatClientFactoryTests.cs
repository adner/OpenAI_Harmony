using System;
using FluentAssertions;
using HarmonyTest.Services;
using Microsoft.Extensions.AI;
using Xunit;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using System.Runtime.CompilerServices;

namespace HarmonyTest.Tests.Services;

public class OpenAIChatClientFactoryTests
{
    private readonly ITestOutputHelper _output;

    private string localEndpoint = "http://127.0.0.1:63484/v1"; 

    public OpenAIChatClientFactoryTests(ITestOutputHelper output)
    {
        _output = output;
    }
    [Fact]
    public void BuildClientOptions_SetsEndpoint()
    {
        var uri = new Uri("https://example.test/v1/");
        var options = OpenAIChatClientFactory.BuildClientOptions(uri);
        options.Endpoint.Should().Be(uri);
    }

    [Theory]
    [InlineData("", "key", "model")]
    [InlineData("https://example.test/v1", "", "model")]
    [InlineData("https://example.test/v1", "key", "")]
    public void Create_InvalidInputs_Throws(string baseUrl, string apiKey, string model)
    {
        Action act = () => OpenAIChatClientFactory.Create(baseUrl, apiKey, model);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_ValidInputs_ReturnsIChatClient()
    {
        // Arrange
        var baseUrl = "https://example.test/v1";
        var apiKey = "test-key";
        var model = "gpt-4o-mini";

        // Act
        IChatClient client = OpenAIChatClientFactory.Create(baseUrl, apiKey, model);

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LMStudio_LocalEndpoint_Responds()
    {
        var baseUrl = localEndpoint; 
        var apiKey = "lm-studio"; // LM Studio typically accepts any non-empty key
    var model = GetLmStudioModel();

        // Skip if endpoint isn't reachable to keep CI/tests green when LM Studio isn't running
        var uri = new Uri(baseUrl);
        if (!await IsReachableAsync(uri.Host, uri.Port, TimeSpan.FromSeconds(1)))
        {
            _output.WriteLine("LM Studio endpoint not reachable at http://localhost:1234. Skipping integration call.");
            return; // soft-skip when service isn't running
        }

        var client = OpenAIChatClientFactory.Create(baseUrl, apiKey, model);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        ChatResponse response;
        try
        {
            response = await client.GetResponseAsync("Hello!", cancellationToken: cts.Token);
        }
        catch (Exception ex) when (ex.Message.Contains("model_not_found", StringComparison.OrdinalIgnoreCase))
        {
            _output.WriteLine("LM Studio model not found. Skipping test.");
            return;
        }

        response.Should().NotBeNull();
        response.ToString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LMStudio_LocalEndpoint_Streams()
    {
        var baseUrl = localEndpoint; // LM Studio local API
        var apiKey = "lm-studio"; // LM Studio typically accepts any non-empty key
    var model = GetLmStudioModel();

        var uri = new Uri(baseUrl);
        if (!await IsReachableAsync(uri.Host, uri.Port, TimeSpan.FromSeconds(1)))
        {
            _output.WriteLine("LM Studio endpoint not reachable at http://localhost:1234. Skipping streaming test.");
            return; // soft-skip when service isn't running
        }

        var client = OpenAIChatClientFactory.Create(baseUrl, apiKey, model);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        string assembled = string.Empty;
        try
        {
            await foreach (var update in client.GetStreamingResponseAsync("Hello!", cancellationToken: cts.Token))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    assembled += update.Text;
                    _output.WriteLine(update.Text);
                }
            }
        }
        catch (Exception ex) when (ex.Message.Contains("model_not_found", StringComparison.OrdinalIgnoreCase))
        {
            _output.WriteLine("LM Studio model not found. Skipping streaming test.");
            return;
        }

        assembled.Should().NotBeNullOrWhiteSpace();
    }

    private static async Task<bool> IsReachableAsync(string host, int port, TimeSpan timeout)
    {
        try
        {
            using var tcpClient = new TcpClient();
            var connectTask = tcpClient.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeout));
            return completed == connectTask && tcpClient.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static string GetLmStudioModel()
    {
        // Prefer environment configuration; fall back to the previously used model ID
        return Environment.GetEnvironmentVariable("LMSTUDIO_MODEL")
               ?? Environment.GetEnvironmentVariable("LM_STUDIO_MODEL")
               ?? "gpt-oss-20b-cuda-gpu";
    }
}
