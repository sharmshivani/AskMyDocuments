namespace AskMyDocuments.Core;

/// <summary>All configuration comes from environment variables, so nothing secret lives in code.</summary>
public sealed record RagSettings(
    string OpenAiEndpoint,
    string EmbeddingDeployment,
    string ChatDeployment,
    string SearchEndpoint,
    string IndexName,
    int EmbeddingDimensions,
    int ChunkWords,
    int ChunkOverlapWords,
    int TopK)
{
    public static RagSettings FromEnvironment()
    {
        static string Required(string key) =>
            Environment.GetEnvironmentVariable(key)
            ?? throw new InvalidOperationException($"Missing environment variable: {key}");

        static int Int(string key, int fallback) =>
            int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : fallback;

        return new RagSettings(
            Required("AZURE_OPENAI_ENDPOINT"),
            Required("AZURE_OPENAI_EMBEDDING_DEPLOYMENT"),
            Required("AZURE_OPENAI_CHAT_DEPLOYMENT"),
            Required("AZURE_SEARCH_ENDPOINT"),
            Environment.GetEnvironmentVariable("AZURE_SEARCH_INDEX") ?? "rag-poc",
            Int("EMBEDDING_DIMENSIONS", 1536),
            Int("CHUNK_WORDS", 120),
            Int("CHUNK_OVERLAP_WORDS", 20),
            Int("TOP_K", 4));
    }
}
