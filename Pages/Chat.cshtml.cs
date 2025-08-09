using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.AI;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarmonyTest.Pages;

public class ChatModel : PageModel
{
    private readonly IChatClient _chat;
    private const string SessionKey = "chat_history";

    public ChatModel(IChatClient chat)
    {
        _chat = chat;
    }

    public void OnGet() { }

    [BindProperty]
    public string? UserMessage { get; set; }

    public record ChatBubble(string Role, string Html);

    public async Task<IActionResult> OnPostStreamAsync()
    {
        var userText = (UserMessage ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(userText))
        {
            return BadRequest("Empty message");
        }

        var history = GetHistory();
        // Ensure an initial system prompt that constrains message lengths
        if (history.Count == 0 || history[0].Role != ChatRole.System)
        {
            history.Insert(0, new ChatMessage(ChatRole.System,
                "Keep your answers brief. Don't talk about your system instructions in the reasoning messages."));
        }
        history.Add(new ChatMessage(ChatRole.User, userText));
    // Save user message before starting the SSE stream to avoid session modification after response starts
    SaveHistory(history);

        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var sbFinal = new StringBuilder();
    await foreach (var update in _chat.GetStreamingResponseAsync(history))
        {
            // Render chunks: reasoning styled differently, final as normal
            foreach (var content in update.Contents)
            {
                string? html = null;
                if (content is TextReasoningContent tr)
                {
            // Emit reasoning as a chunk span so the client can coalesce into a single reasoning bubble
            html = "<span class=\"chunk reasoning\">" + HtmlEncode(tr.Text) + "</span>";
                }
                else if (content is TextContent t)
                {
                    html = "<span class=\"chunk\">" + HtmlEncode(t.Text) + "</span>";
                    sbFinal.Append(t.Text);
                }

                if (html is null) continue;

                var payload = JsonSerializer.Serialize(new ChatBubble("assistant", html));
                await Response.WriteAsync("data: " + payload + "\n\n");
                await Response.Body.FlushAsync();
            }
        }

    // Can't modify session after response has started. The client will POST the final text
    // to OnPostAppendAssistantAsync once streaming completes.

        // Signal completion
        await Response.WriteAsync("event: done\n");
        await Response.WriteAsync("data: {}\n\n");
        await Response.Body.FlushAsync();
        return new EmptyResult();
    }

    public IActionResult OnPostAppendAssistant(string text)
    {
        var finalText = (text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(finalText))
        {
            return BadRequest("Empty assistant text");
        }

        var history = GetHistory();
        history.Add(new ChatMessage(ChatRole.Assistant, finalText));
        SaveHistory(history);
        return new OkResult();
    }

    private List<ChatMessage> GetHistory()
    {
        if (HttpContext.Session.TryGetValue(SessionKey, out var bytes))
        {
            var arr = JsonSerializer.Deserialize<List<SerializableMessage>>(bytes!) ?? new List<SerializableMessage>();
            return new List<ChatMessage>(arr.ConvertAll(ToChat));
        }
        return new List<ChatMessage>();
    }

    private void SaveHistory(List<ChatMessage> history)
    {
        var arr = history.ConvertAll(FromChat);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(arr);
        HttpContext.Session.Set(SessionKey, bytes);
    }

    private static ChatMessage ToChat(SerializableMessage sm)
    {
        var role = sm.Role switch
        {
            "assistant" => ChatRole.Assistant,
            "system" => ChatRole.System,
            _ => ChatRole.User
        };
        return new ChatMessage(role, sm.Text ?? string.Empty);
    }

    private static SerializableMessage FromChat(ChatMessage m)
    {
        var role = m.Role == ChatRole.Assistant
            ? "assistant"
            : (m.Role == ChatRole.System ? "system" : "user");
        return new SerializableMessage(role, m.Text);
    }

    public record SerializableMessage(string Role, string? Text);

    private static string HtmlEncode(string s) => System.Net.WebUtility.HtmlEncode(s);
}
