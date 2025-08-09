using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HarmonyTest.Services;
using Microsoft.Extensions.AI;
using Xunit;

namespace HarmonyTest.Tests.Services;

public class HarmonyCowsParsingTests
{
    [Fact]
    public async Task Parses_Cows_Harmony_Response()
    {
        var harmony = "<|channel|>analysis<|message|>The user asks: Are all cows mammals? The answer: yes, all species of cows are mammals (the animal species Bos taurus). The question likely intends clarifying. So answer: yes, all cows are mammals, as they are placental mammals, giving live births, etc. Also maybe mention taxonomy etc.<|end|><|start|>assistant<|channel|>final<|message|>**Short answer:** Yes—every cow you find in the world today is a mammal.";

        var inner = new StubClient(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, new AIContent[] { new TextContent(harmony) })
        ));
        var client = new HarmonyChatClient(inner);

        var resp = await client.GetResponseAsync("Are all cows mammals");

        resp.Messages.Should().HaveCount(1);
        var msg = resp.Messages[0];
        msg.Contents.Should().HaveCount(2);
        msg.Contents[0].Should().BeOfType<TextReasoningContent>();
        msg.Contents[1].Should().BeOfType<TextContent>().Which.Text.Should().StartWith("**Short answer:** Yes");
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
