// AgenticSdlc.Domain/Pipeline/UserStory.cs
// Phase 3 — Input for the whole pipeline (free-form user story or requirement statement).

namespace AgenticSdlc.Domain.Pipeline;

/// <summary>
/// Input for the pipeline: a single natural-language user story + short parameters.
/// </summary>
/// <param name="Description">User story / requirement statement text. Required, non-empty.</param>
/// <param name="NMax">Maximum number of QA loop iterations (clamped in the PipelineOrchestrator). Default <c>3</c>.</param>
/// <param name="Locale">Preferred language code for the output (e.g. <c>"vi-VN"</c>, <c>"en-US"</c>). Default <c>"vi-VN"</c>.</param>
public sealed record UserStory(string Description, int NMax = 3, string Locale = "vi-VN")
{
    /// <summary>Validates the input. Throws <see cref="System.ArgumentException"/> if invalid.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Description))
        {
            throw new System.ArgumentException("Description must not be null or whitespace.", nameof(Description));
        }

        if (NMax is < 1 or > 10)
        {
            throw new System.ArgumentException("NMax must be in [1, 10].", nameof(NMax));
        }

        if (string.IsNullOrWhiteSpace(Locale))
        {
            throw new System.ArgumentException("Locale must not be null or whitespace.", nameof(Locale));
        }
    }
}
