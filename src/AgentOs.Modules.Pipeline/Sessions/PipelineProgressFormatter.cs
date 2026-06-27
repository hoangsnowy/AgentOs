// Coherence Phase 2 (A2a) — formats a pipeline progress event into a one-line message for the Board
// live session-run feed, so a Quality run reads as a sequence of agent steps (same surface the Quick
// engine's runner steps use). Pure + static so it is unit-tested without a Razor circuit.

using System;
using AgentOs.Domain.Pipeline;

namespace AgentOs.Modules.Pipeline.Sessions;

/// <summary>Maps a <see cref="PipelineProgressEvent"/> to a one-line Board feed message.</summary>
public static class PipelineProgressFormatter
{
    /// <summary>One line: iteration · stage · (message, or the phase when there is no message).</summary>
    public static string Describe(PipelineProgressEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var detail = string.IsNullOrWhiteSpace(e.Message) ? e.Phase.ToString() : e.Message;
        return $"Iter {e.Iteration} · {e.Stage}: {detail}";
    }
}
