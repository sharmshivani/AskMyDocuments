using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;

namespace AskMyDocuments.Core;

public sealed record Hit(string Id, string Source, int ChunkIndex, string Content, double Score);

/// <summary>Everything that talks to Azure AI Search: create the index, upload chunks, run hybrid search.</summary>
public sealed class VectorStore(SearchIndexClient indexClient, SearchClient searchClient, string indexName, int dimensions)
{
    private const string VectorField = "contentVector";

    public async Task EnsureIndexAsync(CancellationToken ct = default)
    {
        var index = new SearchIndex(indexName)
        {
            Fields =
            {
                new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft }, // keyword (BM25) side
                new SearchableField("source") { IsFilterable = true, IsFacetable = true },
                new SimpleField("chunkIndex", SearchFieldDataType.Int32) { IsFilterable = true },
                new SearchField(VectorField, SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    IsSearchable = true,                      // required for vector fields
                    VectorSearchDimensions = dimensions,      // MUST equal the embedding model's output size
                    VectorSearchProfileName = "vector-profile"
                }
            },
            VectorSearch = new VectorSearch
            {
                Algorithms = { new HnswAlgorithmConfiguration("hnsw") }, // approximate nearest-neighbour graph
                Profiles = { new VectorSearchProfile("vector-profile", "hnsw") }
            }
        };
        await indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: ct);
    }

    public async Task UploadAsync(IReadOnlyList<Chunk> chunks, IReadOnlyList<float[]> vectors, CancellationToken ct = default)
    {
        var docs = chunks.Select((c, i) => new SearchDocument
        {
            ["id"] = c.Id,
            ["content"] = c.Text,
            ["source"] = c.Source,
            ["chunkIndex"] = c.Index,
            [VectorField] = vectors[i]
        });
        foreach (var batch in docs.Chunk(100))
            await searchClient.UploadDocumentsAsync(batch, cancellationToken: ct);
    }

    /// <summary>Hybrid search: keyword match on the text AND nearest vectors, fused by the service (RRF).</summary>
    public async Task<IReadOnlyList<Hit>> SearchAsync(string queryText, float[] queryVector, int k, CancellationToken ct = default)
    {
        var options = new SearchOptions
        {
            Size = k,
            Select = { "id", "content", "source", "chunkIndex" },
            VectorSearch = new VectorSearchOptions
            {
                Queries = { new VectorizedQuery(queryVector) { KNearestNeighborsCount = k, Fields = { VectorField } } }
            }
        };

        var response = await searchClient.SearchAsync<SearchDocument>(queryText, options, ct);
        var hits = new List<Hit>();
        await foreach (var r in response.Value.GetResultsAsync())
        {
            hits.Add(new Hit(
                (string)r.Document["id"],
                (string)r.Document["source"],
                Convert.ToInt32(r.Document["chunkIndex"]),
                (string)r.Document["content"],
                r.Score ?? 0));
        }
        return hits;
    }
}
