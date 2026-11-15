using System.Diagnostics;
using System.Text.Json;
using AskMyDocuments.Core;

var (rag, _, _) = RagFactory.Create(RagSettings.FromEnvironment());

var path = args.Length > 0 ? args[0] : "eval/questions.json";
var items = JsonSerializer.Deserialize<List<EvalItem>>(
    File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

var passed = 0;
var latencies = new List<long>();
long promptTokens = 0, completionTokens = 0;

foreach (var item in items)
{
    var sw = Stopwatch.StartNew();
    var answer = await rag.AskAsync(item.Question);
    latencies.Add(sw.ElapsedMilliseconds);
    promptTokens += answer.PromptTokens;
    completionTokens += answer.CompletionTokens;

    // Answerable question: the expected document must be among the retrieved chunks (retrieval hit@k).
    // Unanswerable question: the model must refuse instead of inventing something (groundedness check).
    var ok = item.ExpectedSource is null
        ? answer.Text.Contains("don't know", StringComparison.OrdinalIgnoreCase)
        : answer.Citations.Any(c => c.Source == item.ExpectedSource);

    if (ok) passed++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {sw.ElapsedMilliseconds,5} ms  {item.Question}");
}

latencies.Sort();
long Percentile(double p) => latencies[Math.Max(0, (int)Math.Ceiling(p * latencies.Count) - 1)];

Console.WriteLine();
Console.WriteLine($"Pass rate:        {passed}/{items.Count} ({100.0 * passed / items.Count:F0}%)");
Console.WriteLine($"Latency p50/p95:  {Percentile(0.50)} ms / {Percentile(0.95)} ms");
Console.WriteLine($"Avg tokens/query: {promptTokens / items.Count} prompt, {completionTokens / items.Count} completion");

record EvalItem(string Question, string? ExpectedSource);
