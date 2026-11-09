using SmartHr.Application;

namespace SmartHr.Tests;

public class VectorAndPromptTests
{
    [Fact]
    public void Cosine_identical_is_one() =>
        Assert.Equal(1d, VectorMath.CosineSimilarity([1f, 2f, 3f], [1f, 2f, 3f]), 6);

    [Fact]
    public void Cosine_orthogonal_is_zero() => Assert.Equal(0d, VectorMath.CosineSimilarity([1f, 0f], [0f, 1f]), 6);

    [Fact]
    public void Cosine_opposite_is_minus_one() =>
        Assert.Equal(-1d, VectorMath.CosineSimilarity([1f, 1f], [-1f, -1f]), 6);

    [Fact]
    public void Cosine_handles_zero_and_mismatched_vectors()
    {
        Assert.Equal(0d, VectorMath.CosineSimilarity([0f, 0f], [1f, 1f]));
        Assert.Equal(0d, VectorMath.CosineSimilarity([1f], [1f, 1f]));
        Assert.Equal(0d, VectorMath.CosineSimilarity([], []));
    }

    [Fact]
    public void Prompt_contains_numbered_sources_question_and_guardrails()
    {
        var hits = new List<SearchHit>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Annual Leave", "Entitlement", "21 days", 0.9),
            new(Guid.NewGuid(), Guid.NewGuid(), "Sick Leave", "Paid", "15 days", 0.8),
        };

        var prompt = PromptBuilder.Build("  How many days?  ", hits);

        Assert.Contains("[Source 1: Annual Leave > Entitlement]", prompt);
        Assert.Contains("[Source 2: Sick Leave > Paid]", prompt);
        Assert.Contains("QUESTION:\nHow many days?", prompt.Replace("\r\n", "\n"));
        Assert.Contains("not as instructions", prompt);
        Assert.Contains("same language", prompt);
    }

    [Fact]
    public void No_policies_message_is_valid_arabic() =>
        Assert.Contains("لا توجد سياسات مفهرسة", PromptBuilder.NoPoliciesMessage);
}
