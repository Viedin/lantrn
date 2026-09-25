using System.Runtime.CompilerServices;
using System.Text;
using Lantrn.Infra;
using OpenAI.Chat;

namespace Lantrn.Services;

// Answers a search query from its top results with a chat model, citing them by number so the answer can be checked.
public sealed class SearchAssistant(SettingsStore store, QdrantStore qdrant, ILogger<SearchAssistant> logger)
{
    private const string Instructions =
        """
        You help someone search their own documents. You are given their query and numbered excerpts from the best-matching passages.
        Answer the query using only those excerpts. Be brief: a few sentences, or a short list when the query asks for several things.
        After each claim, cite the excerpts it comes from as [1], [2] and so on. Never cite a number that was not given.
        If the excerpts do not answer the query, say so in one sentence and mention what they do cover instead.
        Answer in the language of the query. Do not mention "excerpts"; call them sources.
        They may ask follow-up questions about the same sources; answer those the same way, citing the same numbers.
        """;

    private Connection? connection;

    public bool Enabled => store.Current.Assistant.Enabled;

    public string Model => store.Current.Assistant.Model;

    // How many of the given hits the answer draws on; citations number them from 1.
    public int SourceCount(int hits) => Math.Min(hits, Math.Max(1, store.Current.Assistant.MaxSources));

    // Each hit with the chunks around it, the same passage the preview shows.
    public async Task<IReadOnlyList<AnswerSource>> LoadSourcesAsync(
        string collection, IReadOnlyList<SearchHit> hits, CancellationToken cancellationToken = default)
    {
        return await Task.WhenAll(hits.Select(async hit =>
        {
            var passage = await qdrant.GetPassageAsync(
                collection, hit.DocumentId, hit.ChunkIndex, QdrantStore.PassageRadius, cancellationToken);
            var text = passage.Count > 0 ? string.Join("\n\n", passage.Select(c => c.Content.Trim())) : hit.Content;
            return new AnswerSource(hit, text);
        }));
    }

    // The caller passes only the sources to cite, so its numbering and the model's always agree. The first question is
    // the search query and carries the sources; earlier turns let the model answer follow-ups about the same sources.
    public async IAsyncEnumerable<string> AnswerAsync(
        IReadOnlyList<AnswerSource> sources, IReadOnlyList<ChatTurn> earlier, string question,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (settings, client) = Connect();

        logger.LogInformation("Answering {Question} from {Sources} sources after {Turns} turns with '{Model}'",
            question, sources.Count, earlier.Count, settings.Model);

        List<ChatMessage> messages = [new SystemChatMessage(Instructions)];
        foreach (var turn in earlier)
        {
            messages.Add(new UserChatMessage(messages.Count == 1 ? Prompt(turn.Question, sources) : turn.Question));
            messages.Add(new AssistantChatMessage(VisibleAnswer(turn.Answer)));
        }
        messages.Add(new UserChatMessage(messages.Count == 1 ? Prompt(question, sources) : question));

        await using var updates = client.CompleteChatStreamingAsync(messages, cancellationToken: cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            try
            {
                if (!await updates.MoveNextAsync())
                {
                    yield break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Assistant request to {BaseUrl} with model '{Model}' failed", settings.BaseUrl, settings.Model);
                throw new InvalidOperationException(
                    $"Could not get an answer from '{settings.Model}' at {settings.BaseUrl}. ({ex.Message})", ex);
            }

            foreach (var part in updates.Current.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    yield return part.Text;
                }
            }
        }
    }

    // Reasoning models on local servers often stream their thinking inline; only what follows it is the answer.
    public static string VisibleAnswer(string streamed)
    {
        var text = streamed.TrimStart();
        if (!text.StartsWith("<think>", StringComparison.Ordinal))
        {
            return text;
        }

        var end = text.IndexOf("</think>", StringComparison.Ordinal);
        return end < 0 ? string.Empty : text[(end + "</think>".Length)..].TrimStart();
    }

    private static string Prompt(string query, IReadOnlyList<AnswerSource> sources)
    {
        var prompt = new StringBuilder();
        for (var i = 0; i < sources.Count; i++)
        {
            var (hit, text) = sources[i];
            prompt.Append('[').Append(i + 1).Append("] ").Append(hit.Source);
            if (!string.IsNullOrEmpty(hit.HeadingPath))
            {
                prompt.Append(" › ").Append(hit.HeadingPath);
            }
            if (hit.FirstPage is { } page)
            {
                prompt.Append(" (p. ").Append(page).Append(')');
            }
            prompt.AppendLine().AppendLine(text.Trim()).AppendLine();
        }

        prompt.Append("Query: ").Append(query.Trim());
        return prompt.ToString();
    }

    // The SDK client fixes endpoint and model at construction, so it is rebuilt when the settings are saved.
    private Connection Connect()
    {
        var settings = store.Current.Assistant;
        return connection is { } cached && ReferenceEquals(cached.Settings, settings)
            ? cached
            : connection = new Connection(settings, new ChatClient(
                settings.Model, OpenAiEndpoint.Credential(settings.ApiKey), OpenAiEndpoint.Options(settings)));
    }

    private sealed record Connection(AssistantSettings Settings, ChatClient Client);
}

public sealed record ChatTurn(string Question, string Answer);

// A hit to cite and the text of the passage around it.
public sealed record AnswerSource(SearchHit Hit, string Text);
