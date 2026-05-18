// AgenticSdlc.Domain/Pipeline/UserStory.cs
// Phase 3 — Input của toàn pipeline (free-form user story hoặc requirement statement).

namespace AgenticSdlc.Domain.Pipeline;

/// <summary>
/// Input cho pipeline: 1 user story ngôn ngữ tự nhiên + tham số ngắn.
/// </summary>
/// <param name="Description">Nội dung user story / requirement statement. Bắt buộc, không rỗng.</param>
/// <param name="NMax">Số iteration tối đa của QA loop (clamp tại PipelineOrchestrator). Mặc định <c>3</c>.</param>
/// <param name="Locale">Mã ngôn ngữ ưu tiên cho output (vd <c>"vi-VN"</c>, <c>"en-US"</c>). Mặc định <c>"vi-VN"</c>.</param>
public sealed record UserStory(string Description, int NMax = 3, string Locale = "vi-VN")
{
    /// <summary>Validate input. Ném <see cref="System.ArgumentException"/> nếu sai.</summary>
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
