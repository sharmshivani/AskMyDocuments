using Azure.Identity;
using Azure.Storage.Blobs;

namespace RagPoc.Ingest;

public sealed record SourceDocument(string Name, string Text);

/// <summary>Reads .md/.txt files from Blob Storage (if BLOB_CONTAINER_URI is set) or from a local folder.</summary>
public static class DocumentSource
{
    private static readonly string[] Extensions = [".md", ".txt"];

    public static async Task<IReadOnlyList<SourceDocument>> LoadAsync(string localFolder)
    {
        var blobUri = Environment.GetEnvironmentVariable("BLOB_CONTAINER_URI");
        return string.IsNullOrWhiteSpace(blobUri)
            ? await LoadLocalAsync(localFolder)
            : await LoadBlobAsync(blobUri);
    }

    private static async Task<IReadOnlyList<SourceDocument>> LoadLocalAsync(string folder)
    {
        var docs = new List<SourceDocument>();
        foreach (var path in Directory.EnumerateFiles(folder).Where(IsSupported))
            docs.Add(new SourceDocument(Path.GetFileName(path), await File.ReadAllTextAsync(path)));
        return docs;
    }

    private static async Task<IReadOnlyList<SourceDocument>> LoadBlobAsync(string containerUri)
    {
        var container = new BlobContainerClient(new Uri(containerUri), new DefaultAzureCredential());
        var docs = new List<SourceDocument>();
        await foreach (var item in container.GetBlobsAsync())
        {
            if (!IsSupported(item.Name)) continue;
            var content = await container.GetBlobClient(item.Name).DownloadContentAsync();
            docs.Add(new SourceDocument(item.Name, content.Value.Content.ToString()));
        }
        return docs;
    }

    private static bool IsSupported(string name) =>
        Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
}
