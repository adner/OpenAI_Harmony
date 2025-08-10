using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using System.Text.RegularExpressions;
using System.Text;

namespace HarmonyTest.Services;

/// <summary>
/// A customizable delegating chat client that inherits from the
/// Microsoft.Extensions.AI DelegatingChatClient base and provides
/// optional response/update transforms or callbacks.
/// </summary>
public partial class HarmonyChatClient : Microsoft.Extensions.AI.DelegatingChatClient
{
    private readonly Func<ChatResponse, ChatResponse>? _responseTransform;
    private readonly Func<ChatResponseUpdate, ChatResponseUpdate>? _updateTransform;
    private readonly Action<ChatResponse>? _onResponse;
    private readonly Action<ChatResponseUpdate>? _onUpdate;

    public HarmonyChatClient(
        IChatClient inner,
        Func<ChatResponse, ChatResponse>? responseTransform = null,
        Func<ChatResponseUpdate, ChatResponseUpdate>? updateTransform = null,
        Action<ChatResponse>? onResponse = null,
        Action<ChatResponseUpdate>? onUpdate = null)
        : base(inner)
    {
        _responseTransform = responseTransform;
        _updateTransform = updateTransform;
        _onResponse = onResponse;
        _onUpdate = onUpdate;
    }

    // Hooks for subclasses
    protected virtual ChatResponse OnResponse(ChatResponse response) => response;
    protected virtual ChatResponseUpdate OnUpdate(ChatResponseUpdate update) => update;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var resp = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        _onResponse?.Invoke(resp);
    var transformed = TransformHarmonyResponse(resp);
        if (_responseTransform is not null)
        {
            transformed = _responseTransform(transformed);
        }
        return OnResponse(transformed);
    }


    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new StringBuilder();

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            // Append any text from this update to the buffer for cross-chunk parsing
            if (update?.Contents is { Count: > 0 })
            {
                foreach (var c in update.Contents)
                {
                    if (c is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                    {
                        buffer.Append(tc.Text);
                    }
                }
            }

            // Extract any complete Harmony segments from the buffer
            var extracted = ExtractHarmonySegments(buffer);
            if (extracted.Count > 0)
            {
                var cloned = new ChatResponseUpdate(update?.Role ?? ChatRole.Assistant, extracted)
                {
                    AuthorName = update?.AuthorName,
                    AdditionalProperties = update?.AdditionalProperties,
                    ConversationId = update?.ConversationId,
                    CreatedAt = update?.CreatedAt,
                    FinishReason = update?.FinishReason,
                    MessageId = update?.MessageId,
                    ModelId = update?.ModelId,
                    RawRepresentation = update?.RawRepresentation,
                    ResponseId = update?.ResponseId,
                };

                _onUpdate?.Invoke(cloned);
                var transformed = _updateTransform is null ? cloned : _updateTransform(cloned);
                yield return OnUpdate(transformed);
            }
        }
    }

    // Note: string-based overloads are extension methods on IChatClient and
    // aren't virtual in the base; overriding the IEnumerable versions is sufficient.
}

public static class UseHarmonyChatClient
{
    public static ChatClientBuilder UseHarmony(
        this ChatClientBuilder builder) =>
        builder.Use(innerClient =>
            new HarmonyChatClient(innerClient)
        );
}
// Harmony parsing helpers within the same class
public partial class HarmonyChatClient
{
    private static readonly Regex s_thinkTagRegex = new("<think>(.*?)</think>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex s_analysisTagRegex = new("<analysis>(.*?)</analysis>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex s_fencedReasoningRegex = new("```(?:analysis|reasoning)\\s*(.*?)```", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex s_reasoningHeaderRegex = new(@"(?s)(?:^|\A)\s*(?:Reasoning|Analysis)\s*:\s*(.*?)\s*(?:^\s*(?:Final|Answer|Output)\s*:\s*|\z)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex s_harmonyChannelsRegex = new(@"<\|channel\|>analysis<\|message\|>(.*?)<\|end\|>.*?<\|start\|>assistant<\|channel\|>final<\|message\|>(.*?)(?:<\|end\|>|<\|return\|>|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex s_harmonyAnalysisOnlyRegex = new(@"<\|channel\|>analysis<\|message\|>(.*?)<\|end\|>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex s_harmonyFinalOnlyRegex = new(@"<\|channel\|>final<\|message\|>(.*?)(?:<\|end\|>|<\|return\|>|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Streaming-specific: require explicit closing tokens so we don't prematurely emit partial segments
    private static readonly Regex s_streamChannelsRegex = new(@"<\|channel\|>analysis<\|message\|>(.*?)<\|end\|>.*?<\|start\|>assistant<\|channel\|>final<\|message\|>(.*?)(?:<\|end\|>|<\|return\|>)", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex s_streamFinalOnlyRegex = new(@"<\|channel\|>final<\|message\|>(.*?)(?:<\|end\|>|<\|return\|>)", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static ChatResponse TransformHarmonyResponse(ChatResponse response)
    {
        if (response?.Messages is null || response.Messages.Count == 0)
        {
            return response ?? new ChatResponse();
        }

        var newMessages = new List<ChatMessage>(response.Messages.Count);
        foreach (var msg in response.Messages)
        {
            if (msg?.Role != ChatRole.Assistant || msg.Contents is null || msg.Contents.Count == 0)
            {
                newMessages.Add(msg ?? new ChatMessage());
                continue;
            }

            var newContents = new List<AIContent>();
            foreach (var content in msg.Contents)
            {
                if (content is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                {
                    var pieces = ParseHarmonyText(tc.Text);
                    if (pieces.Count > 0)
                    {
                        newContents.AddRange(pieces);
                    }
                    else
                    {
                        newContents.Add(content);
                    }
                }
                else
                {
                    newContents.Add(content);
                }
            }

            var newMsg = new ChatMessage(msg.Role, newContents)
            {
                MessageId = msg.MessageId,
                AuthorName = msg.AuthorName,
                RawRepresentation = msg.RawRepresentation,
                AdditionalProperties = msg.AdditionalProperties
            };
            newMessages.Add(newMsg);
        }

        var transformed = new ChatResponse(newMessages)
        {
            ResponseId = response.ResponseId,
            ModelId = response.ModelId,
            CreatedAt = response.CreatedAt,
            FinishReason = response.FinishReason,
            Usage = response.Usage,
            ConversationId = response.ConversationId,
            RawRepresentation = response.RawRepresentation,
            AdditionalProperties = response.AdditionalProperties
        };

        return transformed;
    }

    private static List<AIContent> ParseHarmonyText(string text)
    {
        var results = new List<AIContent>();
        if (string.IsNullOrEmpty(text))
        {
            return results;
        }

        // Fast-path: OpenAI Harmony token format with channels (analysis+final)
        var ch = s_harmonyChannelsRegex.Match(text);
        if (ch.Success)
        {
            var analysis = ch.Groups[1].Value;
            var final = ch.Groups[2].Value;
            if (!string.IsNullOrWhiteSpace(analysis))
            {
                results.Add(new TextReasoningContent(analysis));
            }
            if (!string.IsNullOrWhiteSpace(final))
            {
                results.Add(new TextContent(final));
            }
            return results;
        }

        // Streaming-friendly: analysis-only or final-only segments
        var an = s_harmonyAnalysisOnlyRegex.Match(text);
        if (an.Success)
        {
            var analysis = an.Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(analysis))
            {
                results.Add(new TextReasoningContent(analysis));
                return results;
            }
        }
        var fn = s_harmonyFinalOnlyRegex.Match(text);
        if (fn.Success)
        {
            var final = fn.Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(final))
            {
                results.Add(new TextContent(final));
                return results;
            }
        }

        var matches = new List<Match>();
        foreach (var rx in GetReasoningRegexes())
        {
            foreach (Match m in rx.Matches(text))
            {
                if (m.Success && m.Length > 0)
                {
                    matches.Add(m);
                }
            }
        }

        if (matches.Count == 0)
        {
            var header = s_reasoningHeaderRegex.Match(text);
            if (header.Success)
            {
                var reasoning = header.Groups[1].Value;
                var before = text.Substring(0, header.Index);
                var afterStart = header.Index + header.Length;
                var after = afterStart < text.Length ? text.Substring(afterStart) : string.Empty;

                if (!string.IsNullOrWhiteSpace(before))
                {
                    results.Add(new TextContent(before));
                }
                if (!string.IsNullOrWhiteSpace(reasoning))
                {
                    results.Add(new TextReasoningContent(reasoning));
                }
                if (!string.IsNullOrWhiteSpace(after))
                {
                    results.Add(new TextContent(after));
                }
                return results;
            }

            return results; // caller will keep original content
        }

        matches.Sort((a, b) => a.Index.CompareTo(b.Index));
        var normalized = new List<(int start, int length, string value)>();
        int cursor = 0;
        foreach (var m in matches)
        {
            if (normalized.Count == 0)
            {
                normalized.Add((m.Index, m.Length, ExtractGroupOrValue(m)));
            }
            else
            {
                var last = normalized[^1];
                if (m.Index >= last.start && m.Index < last.start + last.length)
                {
                    continue;
                }
                normalized.Add((m.Index, m.Length, ExtractGroupOrValue(m)));
            }
        }

        foreach (var seg in normalized)
        {
            if (cursor < seg.start)
            {
                var plain = text.Substring(cursor, seg.start - cursor);
                if (!string.IsNullOrEmpty(plain))
                {
                    results.Add(new TextContent(plain));
                }
            }
            if (!string.IsNullOrEmpty(seg.value))
            {
                results.Add(new TextReasoningContent(seg.value));
            }
            cursor = seg.start + seg.length;
        }
        if (cursor < text.Length)
        {
            var tail = text.Substring(cursor);
            if (!string.IsNullOrEmpty(tail))
            {
                results.Add(new TextContent(tail));
            }
        }

        return results;

        static string ExtractGroupOrValue(Match m)
        {
            return m.Groups.Count > 1 ? m.Groups[1].Value : m.Value;
        }
    }

    private static IEnumerable<Regex> GetReasoningRegexes()
    {
        yield return s_thinkTagRegex;
        yield return s_analysisTagRegex;
        yield return s_fencedReasoningRegex;
    }

    private static ChatResponseUpdate TransformHarmonyUpdate(ChatResponseUpdate update)
    {
        if (update is null || update.Contents is null || update.Contents.Count == 0)
        {
            return update ?? new ChatResponseUpdate();
        }

        var newContents = new List<AIContent>(update.Contents.Count);
        bool changed = false;
        foreach (var content in update.Contents)
        {
            if (content is TextContent tc && !string.IsNullOrEmpty(tc.Text))
            {
                var pieces = ParseHarmonyText(tc.Text);
                if (pieces.Count > 0)
                {
                    newContents.AddRange(pieces);
                    changed = true;
                }
                else
                {
                    newContents.Add(content);
                }
            }
            else
            {
                newContents.Add(content);
            }
        }

        if (!changed)
        {
            return update;
        }

        var cloned = new ChatResponseUpdate(update.Role, newContents)
        {
            AuthorName = update.AuthorName,
            AdditionalProperties = update.AdditionalProperties,
            ConversationId = update.ConversationId,
            CreatedAt = update.CreatedAt,
            FinishReason = update.FinishReason,
            MessageId = update.MessageId,
            ModelId = update.ModelId,
            RawRepresentation = update.RawRepresentation,
            ResponseId = update.ResponseId,
        };

        return cloned;
    }

    private static IList<AIContent> ExtractHarmonySegments(StringBuilder buffer)
    {
        var results = new List<AIContent>();
        if (buffer.Length == 0)
        {
            return results;
        }

        while (true)
        {
            var text = buffer.ToString();

            // Full analysis+final first (only when final has a closing tag)
            var full = s_streamChannelsRegex.Match(text);
            if (full.Success)
            {
                var analysis = full.Groups[1].Value;
                var final = full.Groups[2].Value;
                if (!string.IsNullOrWhiteSpace(analysis))
                {
                    results.Add(new TextReasoningContent(analysis));
                }
                if (!string.IsNullOrWhiteSpace(final))
                {
                    results.Add(new TextContent(final));
                }
                buffer.Remove(full.Index, full.Length);
                continue;
            }

            // Analysis-only
            var an = s_harmonyAnalysisOnlyRegex.Match(text);
            if (an.Success)
            {
                var analysis = an.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(analysis))
                {
                    results.Add(new TextReasoningContent(analysis));
                }
                buffer.Remove(an.Index, an.Length);
                continue;
            }

            // Partial analysis streaming: as soon as the analysis channel starts,
            // stream any newly arrived text even if the closing token hasn't arrived yet.
            const string analysisStart = "<|channel|>analysis<|message|>";
            const string endToken = "<|end|>";
            var startIdx = text.IndexOf(analysisStart, StringComparison.OrdinalIgnoreCase);
            if (startIdx >= 0)
            {
                var contentStart = startIdx + analysisStart.Length;
                // If there's already a closing token, the analysis-only regex above would have matched.
                var endIdx = text.IndexOf(endToken, contentStart, StringComparison.OrdinalIgnoreCase);
                if (endIdx < 0)
                {
                    // No closing token yet; emit whatever content has arrived after the start tag.
                    if (contentStart < text.Length)
                    {
                        var chunk = text.Substring(contentStart);
                        if (chunk.Length > 0)
                        {
                            results.Add(new TextReasoningContent(chunk));
                            // Remove only the emitted chunk, keep the start tag so future chunks append
                            // after it and we can keep streaming without duplicating.
                            buffer.Remove(contentStart, chunk.Length);
                            continue;
                        }
                    }
                    // Start tag present but no content yet; wait for more data.
                }
            }

            // Partial final streaming: as soon as the final channel starts,
            // stream any newly arrived text even if the closing token hasn't arrived yet.
            const string finalStart = "<|channel|>final<|message|>";
            const string returnToken = "<|return|>";
            var finalIdx = text.IndexOf(finalStart, StringComparison.OrdinalIgnoreCase);
            if (finalIdx >= 0)
            {
                var finalContentStart = finalIdx + finalStart.Length;
                var endIdx = text.IndexOf(endToken, finalContentStart, StringComparison.OrdinalIgnoreCase);
                var returnIdx = text.IndexOf(returnToken, finalContentStart, StringComparison.OrdinalIgnoreCase);
                // If either a proper end or return token exists, let the final-only regex handle it
                if (endIdx < 0 && returnIdx < 0)
                {
                    if (finalContentStart < text.Length)
                    {
                        var chunk = text.Substring(finalContentStart);
                        if (chunk.Length > 0)
                        {
                            results.Add(new TextContent(chunk));
                            // Remove only the emitted chunk; keep the start tag so future chunks append.
                            buffer.Remove(finalContentStart, chunk.Length);
                            continue;
                        }
                    }
                    // Start tag present but no content yet; wait for more data.
                }
            }

            // Final-only (only when final has a closing tag)
            var fn = s_streamFinalOnlyRegex.Match(text);
            if (fn.Success)
            {
                var final = fn.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(final))
                {
                    results.Add(new TextContent(final));
                }
                buffer.Remove(fn.Index, fn.Length);
                continue;
            }

            break; // no more complete segments
        }

        return results;
    }
}
