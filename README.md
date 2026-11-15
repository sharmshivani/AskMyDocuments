# RAG proof-of-concept on Azure (.NET 8)

"Ask my documents": questions are answered from your own documents, with citations.
Stack: Azure OpenAI (embeddings + chat), Azure AI Search (hybrid vector + keyword), Blob Storage, .NET 8, Terraform.
Auth is Microsoft Entra ID only (your `az login` locally, Managed Identity when deployed). There are no API keys.

```
INGESTION (offline)   Blob docs -> Chunker -> Embeddings (Azure OpenAI) -> AI Search index
QUERY (per request)   Client -> RAG API -> embed question -> hybrid search -> chat model -> answer + citations
```

## Status: read this first
This project was written without access to your Azure subscription or a NuGet feed, so it has NOT been compiled or run.
Expect to fix small things on first build (package versions, a renamed property, a model name or quota in your region).
That is normal. Debugging it is how you learn the SDKs, and `dotnet build` errors will point to the exact line.

## Run it

Prerequisites: .NET 8 SDK, Terraform >= 1.6, Azure CLI, a subscription with Azure OpenAI access.

```bash
az login
cd infra
cp terraform.tfvars.example terraform.tfvars      # put your subscription id in it
terraform init && terraform apply
cd ..
source scripts/set-env.sh                          # exports endpoints from terraform outputs

# RBAC role assignments take a few minutes to propagate. If you get 401/403, wait and retry.
az storage blob upload-batch --account-name "$(terraform -chdir=infra output -raw storage_account)" \
  -d docs -s sample-docs --auth-mode login

dotnet run --project src/RagPoc.Ingest            # 1. build the index
dotnet run --project src/RagPoc.Api               # 2. start the API (note the port it prints)
curl -s -X POST http://localhost:5000/ask -H "Content-Type: application/json" \
  -d '{"question":"How long is a transfer value quotation valid for?"}'
dotnet run --project src/RagPoc.Eval              # 3. measure quality, latency, tokens
terraform -chdir=infra destroy                     # 4. AI Search bills hourly: destroy when done
```

## What each file does, and why

| File | Role | Why it is built this way |
|---|---|---|
| `infra/main.tf` | Creates all Azure resources and RBAC | Keys disabled; roles scoped to each service (least privilege). Reuses your Terraform/IaC story. |
| `Core/RagSettings.cs` | Reads config from environment | Same code runs locally and in a container; nothing secret in source control. |
| `Core/Chunker.cs` | Splits text into overlapping chunks | Models and indexes work on pieces. Overlap protects sentences cut at a boundary. |
| `Core/Embedder.cs` | Text to vectors, in batches | Batching cuts HTTP calls. Same model for documents and questions. |
| `Core/VectorStore.cs` | Index schema, upload, hybrid search | Vector size must match the model; hybrid search covers exact terms that vectors miss. |
| `Core/RagService.cs` | The RAG loop and the grounding prompt | The system prompt is what forces "answer from context or say you don't know". |
| `Core/RagFactory.cs` | Builds the Azure clients | One place to see authentication: `DefaultAzureCredential`. |
| `Ingest` | Load, chunk, embed, upload | Run on demand; deterministic IDs make re-runs overwrite instead of duplicate. |
| `Api` | `POST /ask`, `GET /health` | Input validation at the edge controls cost and abuse. |
| `Eval` + `eval/questions.json` | Pass rate, p50/p95 latency, tokens | Turns "it seems to work" into numbers you can quote. |

## How embeddings work in this PoC

An embedding model converts text into a list of 1,536 numbers (a vector). Texts with similar meaning end up with vectors that point in similar directions, even with no shared words. "Can I retire at 56?" lands near a chunk about early retirement.

1. Ingestion (`Embedder` called from `Ingest`): each chunk becomes a vector, stored next to its text in the index field `contentVector`.
2. Query (`RagService.AskAsync`): the question is embedded by the same model, and AI Search returns the chunks whose vectors are nearest.
3. The index uses HNSW, a graph structure that finds approximate nearest neighbours quickly instead of comparing against every vector.
4. If you change the embedding model, the vector size and meaning change: recreate the index and re-embed everything.

Why hybrid? Vector search finds meaning but can miss exact tokens like "30,000" or a policy ID. Keyword search finds those. The service runs both and fuses the rankings.

## Decisions to write up as short ADRs (this is what makes it architect-level)
- AI Search vs pgvector/Cosmos DB for vectors: managed hybrid search and scale vs cost and simplicity.
- Chunk size and overlap: what you tried (change `CHUNK_WORDS`, rerun Eval) and what the pass rate showed.
- Top-K: more context helps recall but raises tokens, latency and distraction.
- Entra ID and RBAC instead of keys: rotation, audit and least privilege.
- Where hallucination is controlled: the prompt, low temperature, citations, and the refusal test in Eval.

## Known limits and next steps
- Word-based chunking is an approximation. Next: token-aware, heading-aware splitting.
- Removed or shortened documents leave stale chunks. Next: delete by `source` before re-ingesting.
- No semantic ranker, query rewriting or re-ranking yet.
- Deploy the API to Azure Container Apps with a Managed Identity and the same RBAC roles.
- Add OpenTelemetry to Application Insights: latency, tokens and cost per request.
- Add content-safety checks and a prompt-injection test set.
- The sample documents are fictional. Swap in public documents you know well.
