using AskMyDocuments.Core;
using RagPoc.Ingest;

var settings = RagSettings.FromEnvironment();
var (_, store, embedder) = RagFactory.Create(settings);

var folder = args.Length > 0 ? args[0] : "sample-docs";
var documents = await DocumentSource.LoadAsync(folder);
Console.WriteLine($"Loaded {documents.Count} document(s).");

// 1. Make sure the index exists with the right vector dimensions.
await store.EnsureIndexAsync();

// 2. Split every document into overlapping chunks.
var chunks = documents
    .SelectMany(d => Chunker.Split(d.Name, d.Text, settings.ChunkWords, settings.ChunkOverlapWords))
    .ToList();
Console.WriteLine($"Created {chunks.Count} chunk(s) (size {settings.ChunkWords} words, overlap {settings.ChunkOverlapWords}).");

// 3. Embed all chunks, then 4. upload text + vector + metadata together.
var vectors = await embedder.EmbedAsync(chunks.Select(c => c.Text).ToList());
await store.UploadAsync(chunks, vectors);

Console.WriteLine($"Indexed {chunks.Count} chunk(s) into '{settings.IndexName}'. Vector size: {vectors[0].Length}.");
