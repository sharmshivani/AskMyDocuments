using System.Text.RegularExpressions;

namespace AskMyDocuments.Core;

public sealed record Chunk(string Source, int Index, string Text)
{
    // Azure AI Search keys allow only letters, digits, '_', '-' and '='.
    public string Id => $"{Regex.Replace(Source, "[^A-Za-z0-9_=-]", "_")}-{Index}";
}

/// <summary>
/// Sliding window over words. Words are a cheap stand-in for tokens (~1.3 tokens per English word).
/// The overlap repeats the end of one chunk at the start of the next so a sentence that
/// straddles a boundary is still fully present in at least one chunk.
/// </summary>
public static class Chunker
{
    public static IReadOnlyList<Chunk> Split(string source, string text, int maxWords, int overlapWords)
    {
        if (overlapWords >= maxWords)
            throw new ArgumentException("Overlap must be smaller than the chunk size.");

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var step = maxWords - overlapWords;
        var chunks = new List<Chunk>();

        for (int start = 0, index = 0; start < words.Length; start += step, index++)
        {
            var end = Math.Min(start + maxWords, words.Length);
            chunks.Add(new Chunk(source, index, string.Join(' ', words[start..end])));
            if (end == words.Length) break;
        }
        return chunks;
    }
}
