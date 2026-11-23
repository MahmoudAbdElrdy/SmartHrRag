# Smart HR Policy Assistant

An educational Retrieval-Augmented Generation (RAG) application for HR policies.

## Prerequisites

- .NET 8 SDK
- Docker Desktop with its WSL 2 engine running
- Node.js 22.12 or newer in the 22.x line
- Internet access to download the Ollama image and models

## Run the backend

From PowerShell in this folder:

~~~powershell
docker compose up -d
docker compose exec ollama ollama pull bge-m3
docker compose exec ollama ollama pull llama3.2:3b
dotnet restore src/SmartHr.Api/SmartHr.Api.csproj
dotnet ef database update --project src/SmartHr.Infrastructure --startup-project src/SmartHr.Api
dotnet run --project src/SmartHr.Api --urls http://localhost:5080
~~~

The EF migration is already in src/SmartHr.Infrastructure/Migrations. Do not
run migrations add again unless you are changing the database schema.

- Swagger: http://localhost:5080/swagger
- API health: http://localhost:5080/health

## Run the Angular frontend

Open a second PowerShell terminal in the project root:

~~~powershell
cd frontend
npm.cmd install
npm.cmd start
~~~

Open http://localhost:4200. The Angular development server proxies /api and
/health to the .NET API at port 5080.

## Test the application

1. Add a policy using the sample annual-leave text shown in the UI.
2. Ask: “How many annual leave days does an employee with 3 years of experience get?”
3. Review the answer and the policy chunks cited as sources. The same endpoints
   are available in Swagger.

## Architecture

- Domain: policy documents and chunks.
- Application: ingestion, chunking, embedding, retrieval, and answering.
- Infrastructure: SQL Server, Ollama, and PDF/DOCX extraction adapters.
- API: HTTP endpoints and dependency registration.
- Frontend: Angular single-page app for policy ingestion and Q&A.

See docs/ARCHITECTURE_AR.md for the Arabic architecture and RAG explanation.
The same-retrieval chat-model comparison is in docs/llama-vs-qwen.md.

## Retrieval modes

`Retrieval:Mode` in src/SmartHr.Api/appsettings.json selects how a question finds
policy chunks. Restart the API after changing it. `/api/search` and `/api/ask`
use that mode unless the JSON body sets `mode`.

| Mode | What it does |
| --- | --- |
| `Vector` | Embeds the question with bge-m3 and ranks chunks by cosine similarity. This is the default. |
| `Keyword` | Ranks the same cached chunks with BM25. It does not embed the question. |
| `Hybrid` | Fuses the vector list and the BM25 list with reciprocal rank fusion. `Retrieval:RrfK` defaults to 60. |
| `HybridRerank` | Fuses the lists, then sends the top `Retrieval:CandidatePool` hits (default 20, capped at 32) to a cross-encoder before cutting to top K. |

The score on a hit is cosine similarity, a BM25 score, an RRF score, or a reranker
score, depending on the mode. Switching mode does not require re-embedding.

### Why BM25 in the application

docker-compose uses `mcr.microsoft.com/mssql/server`, which is SQL Server on Linux.
Full-Text Search is not supported on that edition, so a full-text catalog would not
come up in this dev setup. Retrieval already loads every chunk into memory for
cosine search. BM25 runs on that same snapshot: no extra service, and Arabic
normalization can run on the indexed text and the query before tokenization.

Lexical normalization is used only for keyword matching:

- أ إ آ ٱ become ا
- ى becomes ي
- ة becomes ه
- tashkeel and tatweel are removed
- a leading ال is stripped, so الإجازة and the misspelling اجازه share a token

Embeddings and the sources shown in the UI keep the original chunk text.

### Reranker

Reranking stays off until `Retrieval:Reranker:Enabled` is true and `BaseUrl` is
set. The client POSTs to a Hugging Face Text Embeddings Inference compatible
`/rerank` endpoint (`query` and `texts`). Ollama has no rerank API. If the
service is disabled, times out, or cannot be reached, HybridRerank keeps the
fused order.

With Docker running, from the project root:

~~~powershell
docker compose --profile reranker up -d reranker
~~~

The first start downloads `BAAI/bge-reranker-v2-m3` into the `reranker_data`
volume. The CPU image is published on port 8081. Then set:

~~~json
"Retrieval": {
  "Mode": "HybridRerank",
  "Reranker": {
    "Enabled": true,
    "BaseUrl": "http://localhost:8081",
    "Model": "BAAI/bge-reranker-v2-m3"
  }
}
~~~

When the API itself runs in Compose, point `BaseUrl` at `http://reranker:80`.
The model name is for operators. The request body sent to TEI is `query` and
`texts`, not a model field. Check the service with `curl http://localhost:8081/health`.

### Compare retrieval

Seed the sample policies first (start the API while Ollama is running).
`samples/evaluation.json` has the original questions plus harder ones: Egyptian
dialect, misspellings such as اجازه, exact percentages and day counts, and
questions the policies do not answer. Unanswerable questions are stored and
timed, and they are left out of Recall and MRR so they do not count as misses.

The comparison below is Recall@4. `latencyMs` on each question is retrieval
time only (embedding, ranking, and reranking), not answer generation. Vector,
Hybrid, and HybridRerank call Ollama. Keyword does not embed the query, but the
chunks must already be in SQL Server. An API that already has the original
cases picks up the new questions on the next startup.

~~~powershell
Invoke-RestMethod http://localhost:5080/api/evaluate/compare -Method Post -ContentType "application/json" -Body '{"topK":4}'
~~~

One mode:

~~~powershell
Invoke-RestMethod http://localhost:5080/api/evaluate -Method Post -ContentType "application/json" -Body '{"topK":4,"mode":"Hybrid"}'
~~~

The Angular evaluation page runs the same four-mode comparison. This repository
does not record measured Recall or MRR. Those numbers come from a local run
against Ollama and the seeded policies.

## Prototype limitations

This is a learning project. It has no authentication, authorization, audit trail,
or tenant isolation. Vector and keyword retrieval both run in application memory
over chunks loaded from SQL Server. The chunker uses character limits. Do not
expose it publicly or use it for confidential HR data without adding appropriate
security and operational controls.
