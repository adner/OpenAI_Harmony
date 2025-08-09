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

public class HarmonyStreamingManyChunksTests
{
    [Fact]
    public async Task Streaming_Parses_Broken_Analysis_Tokens_Across_Chunks()
    {
        // Simulate broken tokens across updates
        var updates = new List<ChatResponseUpdate>
        {
            new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent>{ new TextContent("<|cha") }),
            new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent>{ new TextContent("nnel|>analysis<|messa") }),
            new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent>{ new TextContent("ge|>part 1 of reasoning ") }),
            new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent>{ new TextContent("… part 2<|end|>") }),
            new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent>{ new TextContent("<|start|>assistant<|channel|>final<|message|>Final text ") }),
            new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent>{ new TextContent("continued<|end|>") }),
        };

        var inner = new StreamingStub(updates);
        var client = new HarmonyChatClient(inner);

        var reasonings = new List<string>();
        var finals = new List<string>();
        await foreach (var u in client.GetStreamingResponseAsync("hi"))
        {
            foreach (var c in u.Contents)
            {
                switch (c)
                {
                    case TextReasoningContent tr:
                        reasonings.Add(tr.Text);
                        break;
                    case TextContent t:
                        finals.Add(t.Text);
                        break;
                }
            }
        }

        reasonings.Should().ContainSingle().Which.Should().Contain("part 1 of reasoning");
        finals.Should().ContainSingle().Which.Should().StartWith("Final text continued");
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
