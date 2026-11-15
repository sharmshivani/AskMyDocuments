using AskMyDocuments.Core;

var builder = WebApplication.CreateBuilder(args);

var (rag, _, _) = RagFactory.Create(RagSettings.FromEnvironment());
builder.Services.AddSingleton(rag);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/ask", async (AskRequest request, RagService service, CancellationToken ct) =>
{
    // Validate at the edge: empty or oversized input never reaches the model (cost + abuse control).
    if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 1000)
        return Results.BadRequest(new { error = "question is required and must be at most 1000 characters" });

    var answer = await service.AskAsync(request.Question.Trim(), ct);
    return Results.Ok(answer);
});

app.Run();

public record AskRequest(string Question);
