using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;

namespace AskMyDocuments.Core;

public static class RagFactory
{
    /// <summary>
    /// Wires the Azure clients. DefaultAzureCredential uses your `az login` locally and a
    /// Managed Identity once deployed, so there are no API keys anywhere in this project.
    /// </summary>
    public static (RagService Rag, VectorStore Store, Embedder Embedder) Create(RagSettings s)
    {
        var credential = new DefaultAzureCredential();

        var openAi = new AzureOpenAIClient(new Uri(s.OpenAiEndpoint), credential);
        var embedder = new Embedder(openAi.GetEmbeddingClient(s.EmbeddingDeployment));

        var searchUri = new Uri(s.SearchEndpoint);
        var store = new VectorStore(
            new SearchIndexClient(searchUri, credential),
            new SearchClient(searchUri, s.IndexName, credential),
            s.IndexName,
            s.EmbeddingDimensions);

        var rag = new RagService(embedder, store, openAi.GetChatClient(s.ChatDeployment), s.TopK);
        return (rag, store, embedder);
    }
}
