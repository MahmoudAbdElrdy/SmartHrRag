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
docker compose exec ollama ollama pull nomic-embed-text
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

## Prototype limitations

This is a learning project. It has no authentication, authorization, audit trail,
or tenant isolation. Retrieval currently loads every chunk vector from SQL Server
and computes cosine similarity in application memory. The chunker uses character
limits and does not overlap adjacent chunks. Do not expose it publicly or use it
for confidential HR data without adding appropriate security and operational controls.
