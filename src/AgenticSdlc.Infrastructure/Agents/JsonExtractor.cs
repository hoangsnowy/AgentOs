// AgenticSdlc.Infrastructure/Agents/JsonExtractor.cs
// Phase 4 — Trích JSON từ LLM response (handle markdown fence, prose wrapping).

using System;
using System.Text.Json;
using AgenticSdlc.Domain.Llm;

namespace AgenticSdlc.Infrastructure.Agents;

/// <summary>
/// Helper extract + deserialize JSON từ LLM raw text. LLM thường wrap JSON trong markdown fence
/// hoặc kèm prose trước/sau — helper này tolerant với 3 dạng phổ biến.
/// </summary>
public static class JsonExtractor
{
    /// <summary>
    /// Default JsonSerializerOptions: camelCase, case-insensitive, allow trailing comma.
    /// </summary>
    public static JsonSerializerOptions DefaultOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Deserialize <paramref name="rawText"/> thành <typeparamref name="T"/>.
    /// Thứ tự thử:
    /// <list type="number">
    ///   <item>Parse trực tiếp.</item>
    ///   <item>Strip markdown fence <c>```json...```</c> nếu có.</item>
    ///   <item>Tìm từ <c>'{'</c> đầu đến <c>'}'</c> cuối, parse substring.</item>
    /// </list>
    /// Ném <see cref="LlmException"/> nếu cả 3 đều fail.
    /// </summary>
    public static T Deserialize<T>(string rawText, string agentName)
    {
        ArgumentNullException.ThrowIfNull(rawText);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        // 1. Direct parse
        if (TryParse<T>(rawText, out var direct))
        {
            return direct!;
        }

        // 2. Strip markdown fence
        var stripped = StripFence(rawText);
        if (stripped is not null && TryParse<T>(stripped, out var afterFence))
        {
            return afterFence!;
        }

        // 3. Substring { ... }
        var sub = ExtractBraced(rawText);
        if (sub is not null && TryParse<T>(sub, out var afterSub))
        {
            return afterSub!;
        }

        throw new LlmException(
            $"{agentName}: response không phải JSON parse được. Raw (truncated 500 chars): {Truncate(rawText, 500)}",
            agentName);
    }

    /// <summary>
    /// Trả về chuỗi JSON đã được "clean" (strip fence + braced) từ <paramref name="rawText"/>
    /// — dùng cho schema validation trước khi deserialize. Ném <see cref="LlmException"/>
    /// nếu không tìm được JSON object hợp lệ.
    /// </summary>
    public static string ExtractJson(string rawText, string agentName)
    {
        ArgumentNullException.ThrowIfNull(rawText);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        if (TryParseJsonNode(rawText))
        {
            return rawText;
        }

        var stripped = StripFence(rawText);
        if (stripped is not null && TryParseJsonNode(stripped))
        {
            return stripped;
        }

        var sub = ExtractBraced(rawText);
        if (sub is not null && TryParseJsonNode(sub))
        {
            return sub;
        }

        throw new LlmException(
            $"{agentName}: response không phải JSON parse được. Raw (truncated 500 chars): {Truncate(rawText, 500)}",
            agentName);
    }

    private static bool TryParseJsonNode(string json)
    {
        try
        {
            using var _ = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParse<T>(string json, out T? value)
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(json, DefaultOptions);
            return value is not null;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Strip ```json ... ``` hoặc ``` ... ``` fence. Trả null nếu không thấy fence.</summary>
    private static string? StripFence(string raw)
    {
        var start = raw.IndexOf("```", StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        // Bỏ qua optional language tag sau ```.
        var newline = raw.IndexOf('\n', start);
        if (newline < 0)
        {
            return null;
        }

        var end = raw.IndexOf("```", newline, StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        return raw.Substring(newline + 1, end - newline - 1).Trim();
    }

    /// <summary>Trích substring từ <c>{</c> đầu đến <c>}</c> cuối. Trả null nếu không thấy.</summary>
    private static string? ExtractBraced(string raw)
    {
        var first = raw.IndexOf('{');
        var last = raw.LastIndexOf('}');
        if (first < 0 || last <= first)
        {
            return null;
        }

        return raw.Substring(first, last - first + 1);
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : string.Concat(s.AsSpan(0, max), "...");
}
