namespace SmartHr.Application;

/// <summary>How <see cref="RagService"/> ranks policy chunks for an answer or an evaluation run.</summary>
public enum RetrievalMode
{
    /// <summary>Cosine similarity of bge-m3 embeddings.</summary>
    Vector = 0,

    /// <summary>In-memory BM25 over chunk text.</summary>
    Keyword = 1,

    /// <summary>Vector and keyword lists fused with reciprocal rank fusion.</summary>
    Hybrid = 2,

    /// <summary>Hybrid fusion, then an optional cross-encoder over the top candidates.</summary>
    HybridRerank = 3,
}

/// <summary>Bound from the Retrieval section of appsettings.</summary>
public sealed class RetrievalOptions
{
    public const string SectionName = "Retrieval";

    public RetrievalMode Mode { get; set; } = RetrievalMode.Vector;

    /// <summary>RRF constant. The fusion score of a hit is sum(1 / (RrfK + rank)).</summary>
    public int RrfK { get; set; } = ReciprocalRankFusion.DefaultK;

    /// <summary>How many fused hits are sent to the reranker before the final top K is cut.</summary>
    public int CandidatePool { get; set; } = 20;

    public double Bm25K1 { get; set; } = 1.2;

    public double Bm25B { get; set; } = 0.75;

    public RerankerOptions Reranker { get; set; } = new();
}

/// <summary>HTTP cross-encoder. Leave Enabled false or BaseUrl empty to skip reranking.</summary>
public sealed class RerankerOptions
{
    public bool Enabled { get; set; }

    /// <summary>Origin of a Hugging Face Text Embeddings Inference server, without the /rerank path.</summary>
    public string BaseUrl { get; set; } = "";

    public string Model { get; set; } = "BAAI/bge-reranker-v2-m3";

    public int TimeoutSeconds { get; set; } = 15;

    public string Path { get; set; } = "/rerank";
}
