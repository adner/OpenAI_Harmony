using System;
using Microsoft.Extensions.AI;
using System.ClientModel;
using OpenAI;
using OpenAI.Chat;

namespace HarmonyTest.Services;

/// <summary>
/// Factory for creating IChatClient instances using an OpenAI-compatible endpoint.
/// </summary>
public static class OpenAIChatClientFactory
{
    /// <summary>
    /// Creates an IChatClient connected to a specified OpenAI-compatible endpoint.
    /// </summary>
    /// <param name="baseUrl">The base URL of the OpenAI-compatible API, e.g., https://api.openai.com/v1 or a proxy URL.</param>
    /// <param name="apiKey">The API key to use for authentication.</param>
    /// <param name="model">The model name, e.g., gpt-4o-mini.</param>
    /// <returns>An IChatClient instance.</returns>
    /// <exception cref="ArgumentException">Thrown when required parameters are missing or invalid.</exception>
    public static IChatClient Create(string baseUrl, string apiKey, string model)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL must be provided", nameof(baseUrl));
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint))
        {
            throw new ArgumentException("Base URL must be a valid absolute URI", nameof(baseUrl));
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key must be provided", nameof(apiKey));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model must be provided", nameof(model));
        }

        var chatClient = CreateOpenAIChatClient(endpoint, apiKey, model);
        return chatClient.AsIChatClient();
    }

    // Internal for unit testing, returns the underlying OpenAI.Chat.ChatClient
    internal static ChatClient CreateOpenAIChatClient(Uri endpoint, string apiKey, string model)
    {
    var options = BuildClientOptions(endpoint);
    var credential = new ApiKeyCredential(apiKey);
    var client = new ChatClient(model, credential, options);
    return client;
    }

    internal static OpenAIClientOptions BuildClientOptions(Uri endpoint)
    {
        return new OpenAIClientOptions
        {
            Endpoint = endpoint
        };
    }
}
