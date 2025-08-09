using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HarmonyTest.Services;
using Microsoft.Extensions.AI;
using Xunit;

namespace HarmonyTest.Tests.Services;

public class HarmonyHarmonyParsingTests
{
    [Fact]
    public async Task Parses_Harmony_Channel_Message_Into_Reasoning_And_Final()
    {
        var harmony = "<|channel|>analysis<|message|>We need to respond politely.<|end|><|start|>assistant<|channel|>final<|message|>Hi there! 👋 How can I help you today?<|return|>";

        var inner = new StubClient(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, new AIContent[] { new TextContent(harmony) })
        ));
        var client = new HarmonyChatClient(inner);

        var resp = await client.GetResponseAsync("hi");

        resp.Messages.Should().HaveCount(1);
        var msg = resp.Messages[0];
        msg.Contents.Should().HaveCount(2);
        msg.Contents[0].Should().BeOfType<TextReasoningContent>().Which.Text.Should().Be("We need to respond politely.");
        msg.Contents[1].Should().BeOfType<TextContent>().Which.Text.Should().Be("Hi there! 👋 How can I help you today?");
    }

    private sealed class StubClient : IChatClient, IDisposable
    {
        private readonly ChatResponse _response;
        public StubClient(ChatResponse response) => _response = response;
        public object? GetService(Type serviceType, object? key = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => Task.FromResult(_response);
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => EmptyAsync();
        public void Dispose() { }
        private static async IAsyncEnumerable<ChatResponseUpdate> EmptyAsync()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
