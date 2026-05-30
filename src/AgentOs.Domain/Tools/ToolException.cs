// Epic E1 — Tool subsystem exception. Distinct from LlmException so the orchestrator's catch
// blocks can distinguish "the model misused a tool" from "the model call itself failed".

namespace AgentOs.Domain.Tools;

/// <summary>Thrown when a tool registration, resolution or invocation cannot complete.</summary>
public sealed class ToolException : System.Exception
{
    public ToolException(string message)
        : base(message)
    {
    }

    public ToolException(string message, System.Exception innerException)
        : base(message, innerException)
    {
    }
}
