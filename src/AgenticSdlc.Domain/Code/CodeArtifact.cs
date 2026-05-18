// AgenticSdlc.Domain/Code/CodeArtifact.cs
// Phase 3 — Output của CodingAgent — source code dạng file collection.

using System.Collections.Generic;

namespace AgenticSdlc.Domain.Code;

/// <summary>
/// Bộ source code sinh bởi <c>ICodingAgent</c>.
/// </summary>
/// <param name="ProjectName">Tên project được sinh (vd <c>ProductCatalog</c>).</param>
/// <param name="Architecture">Kiến trúc áp dụng (mặc định <c>"Clean Architecture"</c>).</param>
/// <param name="Files">Danh sách file (path + content + language).</param>
/// <param name="Notes">Ghi chú thêm từ agent (vd assumption, TODO).</param>
/// <param name="Metrics">Metric agent.</param>
public sealed record CodeArtifact(
    string ProjectName,
    string Architecture,
    IReadOnlyList<CodeFile> Files,
    string? Notes,
    AgentMetrics Metrics);

/// <summary>1 file source code.</summary>
/// <param name="Path">Đường dẫn tương đối (vd <c>src/Domain/Product.cs</c>).</param>
/// <param name="Content">Nội dung file.</param>
/// <param name="Language">Ngôn ngữ (<c>csharp</c>, <c>json</c>, <c>sql</c>, ...).</param>
public sealed record CodeFile(string Path, string Content, string Language = "csharp");
