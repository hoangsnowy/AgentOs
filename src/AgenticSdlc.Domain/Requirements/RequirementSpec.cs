// AgenticSdlc.Domain/Requirements/RequirementSpec.cs
// Phase 3 — Output của RequirementAgent — structured requirement specification.

using System.Collections.Generic;

namespace AgenticSdlc.Domain.Requirements;

/// <summary>
/// Structured requirement specification — output của <c>IRequirementAgent</c>.
/// JSON-serializable, để truyền sang CodingAgent + TestingAgent + QaAgent.
/// </summary>
/// <param name="Title">Tiêu đề ngắn của tính năng.</param>
/// <param name="Summary">1-2 câu tóm tắt.</param>
/// <param name="Stakeholders">Vai trò liên quan (vd "admin", "khách hàng vãng lai").</param>
/// <param name="FunctionalRequirements">Yêu cầu chức năng — vd "Admin tạo sản phẩm với SKU duy nhất".</param>
/// <param name="NonFunctionalRequirements">Yêu cầu phi chức năng — vd "API response p95 ≤ 200ms".</param>
/// <param name="Entities">Entity domain — driver cho CodingAgent sinh model.</param>
/// <param name="Endpoints">Endpoint API — surface cần expose.</param>
/// <param name="AcceptanceCriteria">Tiêu chí chấp nhận — driver cho TestingAgent.</param>
/// <param name="Metrics">Metric agent (token, cost, latency).</param>
public sealed record RequirementSpec(
    string Title,
    string Summary,
    IReadOnlyList<string> Stakeholders,
    IReadOnlyList<string> FunctionalRequirements,
    IReadOnlyList<string> NonFunctionalRequirements,
    IReadOnlyList<EntityDescriptor> Entities,
    IReadOnlyList<EndpointDescriptor> Endpoints,
    IReadOnlyList<string> AcceptanceCriteria,
    AgentMetrics Metrics);

/// <summary>Entity domain mô tả — chuyển thẳng cho CodingAgent sinh class/record.</summary>
/// <param name="Name">Tên entity (PascalCase).</param>
/// <param name="Fields">Danh sách field <c>{Name}: {Type}</c>.</param>
/// <param name="Notes">Ghi chú thêm (constraint, invariant).</param>
public sealed record EntityDescriptor(string Name, IReadOnlyList<string> Fields, string? Notes = null);

/// <summary>API endpoint mô tả.</summary>
/// <param name="Method">HTTP method (<c>GET</c>, <c>POST</c>, ...).</param>
/// <param name="Path">Route template (vd <c>/products/{id}</c>).</param>
/// <param name="Purpose">Mô tả mục đích.</param>
/// <param name="AuthRequired">Có yêu cầu auth không.</param>
public sealed record EndpointDescriptor(string Method, string Path, string Purpose, bool AuthRequired = false);
