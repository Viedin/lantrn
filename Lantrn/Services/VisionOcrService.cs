using System.Diagnostics;
using Lantrn.Infra;
using OpenAI.Chat;

namespace Lantrn.Services;

// Reads images with a vision model rather than Tesseract, which turns photos (receipts, whiteboards) into noise.
public sealed class VisionOcrService(SettingsStore store, ILogger<VisionOcrService> logger)
{
    private const string Instructions =
        """
        You transcribe images into Markdown for a search index.
        Write down every piece of legible text in the image, exactly as written and in its original language.
        Keep the structure: headings for titles, lists for lists, and Markdown tables for tabular content such as receipt line items with their prices.
        Do not translate, summarize, correct or add commentary. Do not wrap the answer in a code block.
        If the image contains no legible text, answer with nothing at all.
        """;

    private Connection? connection;

    public string Model => store.Current.Vision.Model;

    public async Task<string> ReadImageAsync(byte[] bytes, string fileName, CancellationToken cancellationToken = default)
    {
        if (!DocumentExtractor.TryGetContentType(fileName, out var contentType) || !DocumentExtractor.IsImage(fileName))
        {
            throw new NotSupportedException($"'{fileName}' is not a supported image.");
        }

        var (settings, client) = Connect();

        logger.LogInformation("Reading {FileName} ({Bytes:N0} bytes, {ContentType}) with '{Model}'",
            fileName, bytes.Length, contentType, settings.Model);

        List<ChatMessage> messages =
        [
            new SystemChatMessage(Instructions),
            new UserChatMessage(
                ChatMessageContentPart.CreateTextPart("Transcribe this image."),
                ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(bytes), contentType, ChatImageDetailLevel.High)),
        ];

        var stopwatch = Stopwatch.StartNew();
        ChatCompletion completion;
        try
        {
            completion = (await client.CompleteChatAsync(messages, cancellationToken: cancellationToken)).Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Vision request to {BaseUrl} with model '{Model}' failed", settings.BaseUrl, settings.Model);
            throw new InvalidOperationException(
                $"Could not read the image with '{settings.Model}' at {settings.BaseUrl}. Is the server running and a vision model loaded? ({ex.Message})", ex);
        }

        var markdown = StripCodeFence(string.Concat(completion.Content.Select(part => part.Text)).Trim());

        logger.LogInformation("Read {Characters:N0} chars from {FileName} in {Elapsed:N0} ms (finish reason {FinishReason})",
            markdown.Length, fileName, stopwatch.ElapsedMilliseconds, completion.FinishReason);

        return markdown;
    }

    // The SDK client fixes endpoint and model at construction, so it is rebuilt when the settings are saved.
    private Connection Connect()
    {
        var settings = store.Current.Vision;
        return connection is { } cached && ReferenceEquals(cached.Settings, settings)
            ? cached
            : connection = new Connection(settings, new ChatClient(
                settings.Model, OpenAiEndpoint.Credential(settings.ApiKey), OpenAiEndpoint.Options(settings)));
    }

    // Models asked not to fence their answer sometimes do anyway.
    private static string StripCodeFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal) || !text.EndsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var firstNewline = text.IndexOf('\n');
        return firstNewline < 0 ? string.Empty : text[(firstNewline + 1)..^3].Trim();
    }

    private sealed record Connection(VisionSettings Settings, ChatClient Client);
}
