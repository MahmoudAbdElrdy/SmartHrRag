# Smart HR RAG: how the project works

## Architecture

The backend is a modular application that follows Clean Architecture and Ports and Adapters ideas:

- Domain contains policy documents and chunks.
- Application contains use cases such as ingestion, search, and answering, along with interfaces for storage and Ollama.
- Infrastructure implements those interfaces with EF Core, SQL Server, Ollama, and PDF/DOCX text extraction.
- The API exposes HTTP endpoints and connects requests to application services.
- Angular is a separate frontend that calls the API through a development proxy.

The API and Infrastructure depend inward on Application and Domain. Application does not depend directly on SQL Server or Ollama.

## What happens when a policy is added?

1. The UI posts the policy name and text to POST /api/policies/text.
2. RagService passes the text to StructureChunker.
3. The chunker splits at paragraphs and uses Markdown headings as section names.
4. Nearby paragraphs are grouped up to a default limit of about 1,100 characters. Oversized Markdown tables repeat their header when possible.
5. The policy name and section are added to each chunk before embedding, so each vector keeps useful context.
6. OllamaClient sends each chunk to bge-m3 through /api/embed.
7. SQL Server stores the document, its chunks, and each vector as JSON.

## What is chunking?

Chunking breaks a long document into smaller passages that each carry a relatively clear idea. At question time, the application searches passages instead of sending the whole policy to the language model. Splitting at paragraphs and headings usually preserves meaning better than cutting at a fixed character offset.

This learning version uses character limits, not token counts, and does not overlap neighboring chunks. A long sentence or table row can still split. A larger system could use token-aware limits, deliberate overlap, or a parser that understands tables.

## What is an embedding?

An embedding is a vector of numbers representing a text's approximate meaning. Texts with related meanings tend to have vectors that are closer together. During indexing, the application stores a vector for every chunk. At question time, it embeds the question with the same model.

Use the same embedding model for indexing and retrieval. If the model changes, reindex the policies because vector dimensions or meaning can differ.

## What happens when a question is asked?

1. Angular sends the question and topK to POST /api/ask.
2. bge-m3 converts the question to a vector.
3. The application reads chunk vectors from SQL Server and computes cosine similarity against the question vector.
4. It selects the top matching chunks. This step is called retrieval.
5. It packages those passages with policy names, sections, and chunk IDs.
6. It sends the question and context to llama3.2:3b through /api/generate.
7. The prompt asks the model to answer from the policy text and cite labels such as [Source 1]. Angular displays the answer and retrieved sources.

RAG combines semantic retrieval with a language model: retrieval finds policy text and the model turns it into an answer. The policy sources displayed with the response are the evidence; the model's memory is not a policy source.

## Try it

1. Add the sample annual-leave policy from the Angular screen.
2. Ask: “How many annual leave days does an employee with 3 years of experience get?”
3. Check that the answer is 21 days and inspect the cited policy chunk.
4. Ask something the policy does not cover. The assistant should say the information is unavailable instead of inventing a rule.

## Learning-project limitations

- There is no authentication, authorization, or tenant isolation.
- All chunk vectors are loaded from SQL Server and compared in application memory, which will not scale to a large corpus.
- Storing vectors as JSON in SQL Server is a teaching choice; use a vector-capable search engine for larger collections.
- Chunking is not token-aware and has no overlap.
- Docker Compose credentials are for local development only.
