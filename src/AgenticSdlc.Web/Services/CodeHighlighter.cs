// AgenticSdlc.Web/Services/CodeHighlighter.cs
// Phase 7 — "Lightweight" C# syntax highlighting for read-only display. XSS-safe: HTML-encode
// first, then a single regex alternation pass (comment > string > keyword > number) so each character
// belongs to at most 1 token, with no nesting.

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace AgenticSdlc.Web.Services;

/// <summary>A minimal syntax highlighter that returns a render-safe <see cref="MarkupString"/>.</summary>
public static partial class CodeHighlighter
{
    /// <summary>Highlight a C# snippet. Returns a MarkupString (HTML-encoded + wrapped in spans).</summary>
    public static MarkupString Highlight(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return new MarkupString(string.Empty);
        }

        var encoded = WebUtility.HtmlEncode(code);
        var html = TokenRegex().Replace(encoded, static m =>
        {
            if (m.Groups["cm"].Success)
            {
                return $"<span class=\"cm\">{m.Value}</span>";
            }
            if (m.Groups["str"].Success)
            {
                return $"<span class=\"str\">{m.Value}</span>";
            }
            if (m.Groups["kw"].Success)
            {
                return $"<span class=\"kw\">{m.Value}</span>";
            }
            if (m.Groups["num"].Success)
            {
                return $"<span class=\"num\">{m.Value}</span>";
            }
            return m.Value;
        });

        return new MarkupString(html);
    }

    [GeneratedRegex(
        @"(?<cm>//[^\n]*)|(?<str>&quot;.*?&quot;)|(?<kw>\b(?:public|private|protected|internal|sealed|static|class|interface|record|namespace|using|return|new|async|await|var|void|if|else|for|foreach|while|true|false|null|required|init|get|set|this)\b)|(?<num>\b\d+m?\b)")]
    private static partial Regex TokenRegex();
}
