using System;

namespace AgentOs.SharedKernel.Persistence;

/// <summary>Shared bounds for offset/limit pagination so unbounded list reads can't pull a whole table.</summary>
public static class Page
{
    /// <summary>Default page size when a caller passes a non-positive limit.</summary>
    public const int DefaultLimit = 200;

    /// <summary>Hard ceiling on a single page, so a hostile <c>?limit=</c> can't request the world.</summary>
    public const int MaxLimit = 500;

    /// <summary>Clamps a requested limit into <c>[1, MaxLimit]</c> (non-positive → <see cref="DefaultLimit"/>).</summary>
    public static int ClampLimit(int limit) => limit < 1 ? DefaultLimit : Math.Min(limit, MaxLimit);

    /// <summary>Clamps a requested offset to <c>&gt;= 0</c>.</summary>
    public static int ClampOffset(int offset) => offset < 0 ? 0 : offset;
}
