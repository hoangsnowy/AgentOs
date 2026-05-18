// AgenticSdlc.Domain/Testing/TestArtifact.cs
// Phase 3 — Output của TestingAgent — bộ test case xUnit.

using System.Collections.Generic;
using AgenticSdlc.Domain.Code;

namespace AgenticSdlc.Domain.Testing;

/// <summary>
/// Bộ test case sinh bởi <c>ITestingAgent</c>.
/// </summary>
/// <param name="Framework">Framework test (<c>xUnit</c>, <c>NUnit</c>, ...). Mặc định <c>xUnit</c>.</param>
/// <param name="Files">Danh sách file test (path + content).</param>
/// <param name="HappyPathCount">Số test happy-path.</param>
/// <param name="EdgeCaseCount">Số test edge-case.</param>
/// <param name="ErrorCaseCount">Số test error-case.</param>
/// <param name="EstimatedCoveragePercent">Coverage ước tính (%) — agent self-report, KHÔNG đo thật.</param>
/// <param name="Metrics">Metric agent.</param>
public sealed record TestArtifact(
    string Framework,
    IReadOnlyList<CodeFile> Files,
    int HappyPathCount,
    int EdgeCaseCount,
    int ErrorCaseCount,
    int EstimatedCoveragePercent,
    AgentMetrics Metrics)
{
    /// <summary>Tổng số test case (happy + edge + error).</summary>
    public int TotalCount => HappyPathCount + EdgeCaseCount + ErrorCaseCount;
}
