using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HarmonyTest.Services;
using Microsoft.Extensions.AI;
using Xunit;
using Xunit.Abstractions;

namespace HarmonyTest.Tests.Services;

public class HarmonyChatClientTests
{
    private readonly ITestOutputHelper _output;

    public HarmonyChatClientTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Harmony_Transforms_ThinkTags_To_TextReasoningContent()
    {
        // Arrange: inner client returns assistant text with <think> reasoning </think>
        var inner = new StubClient(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, new AIContent[] { new TextContent("Hello<think>internal steps</think> world") })
        ));
        var client = new HarmonyChatClient(inner);

        // Act
        var resp = await client.GetResponseAsync("hi");

        // Assert
        resp.Should().NotBeNull();
        resp.Messages.Should().HaveCount(1);
        var msg = resp.Messages[0];
        msg.Role.Should().Be(ChatRole.Assistant);
        msg.Contents.Should().HaveCount(3); // "Hello", reasoning, " world"
        msg.Contents[0].Should().BeOfType<TextContent>().Which.Text.Should().Be("Hello");
        msg.Contents[1].Should().BeOfType<TextReasoningContent>().Which.Text.Should().Be("internal steps");
        msg.Contents[2].Should().BeOfType<TextContent>().Which.Text.Should().Be(" world");
    }

    private sealed class StubClient : IChatClient, IDisposable
    {
        private readonly ChatResponse _response;
        public StubClient(ChatResponse response) => _response = response;
        public object? GetService(Type serviceType, object? key = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => Task.FromResult(_response);
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => EmptyAsync();
        public void Dispose() { }

        private static async IAsyncEnumerable<ChatResponseUpdate> EmptyAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LMStudio_HarmonyClient_NonStreaming_Responds()
    {
        var baseUrl = "http://127.0.0.1:63484/v1"; // LM Studio local API
        var apiKey = "lm-studio"; // LM Studio typically accepts any non-empty key
        var model = GetLmStudioModel();

        var uri = new Uri(baseUrl);
        if (!await IsReachableAsync(uri.Host, uri.Port, TimeSpan.FromSeconds(1)))
        {
            _output.WriteLine("LM Studio endpoint not reachable at http://localhost:1234. Skipping delegating client non-streaming test.");
            return; // soft-skip when service isn't running
        }

        IChatClient inner = OpenAIChatClientFactory.Create(baseUrl, apiKey, model);
        var client = new HarmonyChatClient(inner);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        ChatResponse response = await client.GetResponseAsync("Hello!", cancellationToken: cts.Token);

        response.Should().NotBeNull();
        response.ToString().Should().NotBeNullOrWhiteSpace();
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
        return Environment.GetEnvironmentVariable("LMSTUDIO_MODEL")
               ?? Environment.GetEnvironmentVariable("LM_STUDIO_MODEL")
               ?? "gpt-oss-20b-cuda-gpu";
    }
}
