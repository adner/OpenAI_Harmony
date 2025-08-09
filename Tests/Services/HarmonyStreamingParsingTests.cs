using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HarmonyTest.Services;
using Microsoft.Extensions.AI;
using Xunit;

namespace HarmonyTest.Tests.Services;

public class HarmonyStreamingParsingTests
{
    [Fact]
    public async Task Streaming_Parses_Analysis_Then_Final_Pieces()
    {
        // arrange a stub that streams analysis-only update, then final-only update
        var updates = new List<ChatResponseUpdate>
        {
            new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent>{ new TextContent("<|channel|>analysis<|message|>thinking...<|end|>") }),
            new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent>{ new TextContent("<|channel|>final<|message|>Done.<|end|>") })
        };
        var inner = new StreamingStub(updates);
        var client = new HarmonyChatClient(inner);

        var seen = new List<ChatResponseUpdate>();
        await foreach (var u in client.GetStreamingResponseAsync("hi"))
        {
            seen.Add(u);
        }

        seen.Should().HaveCount(2);
        seen[0].Contents.Should().ContainSingle().Which.Should().BeOfType<TextReasoningContent>().Which.Text.Should().Be("thinking...");
        seen[1].Contents.Should().ContainSingle().Which.Should().BeOfType<TextContent>().Which.Text.Should().Be("Done.");
    }

    private sealed class StreamingStub : IChatClient, IDisposable
    {
        private readonly List<ChatResponseUpdate> _updates;
        public StreamingStub(List<ChatResponseUpdate> updates) => _updates = updates;
        public object? GetService(Type serviceType, object? key = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "stub")));
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var u in _updates)
            {
                yield return u;
                await Task.Yield();
            }
        }
        public void Dispose() { }
    }
}
