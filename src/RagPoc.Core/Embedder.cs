using OpenAI.Embeddings;

namespace AskMyDocuments.Core;

/// <summary>
/// Turns text into vectors using the Azure OpenAI embedding deployment.
/// The SAME deployment must embed documents (ingestion) and questions (query),
/// otherwise the two live in different "spaces" and distances mean nothing.
/// </summary>
public sealed class Embedder(EmbeddingClient client)
{
    private const int BatchSize = 16; // one HTTP call per 16 texts instead of one per text

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var vectors = new List<float[]>(texts.Count);
        foreach (var batch in texts.Chunk(BatchSize))
        {
            OpenAIEmbeddingCollection result = await client.GenerateEmbeddingsAsync(batch, cancellationToken: ct);
            vectors.AddRange(result.OrderBy(e => e.Index).Select(e => e.ToFloats().ToArray()));
        }
        return vectors;
    }
}
