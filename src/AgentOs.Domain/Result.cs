// A minimal success-or-error result. Used for *expected* failures (e.g. an LLM returning unparseable
// output) so they flow as values instead of exceptions across a hot path, while genuinely exceptional
// faults still throw. Deliberately tiny — no external dependency, BCL only.

using System;

namespace AgentOs.Domain;

/// <summary>Carries either a success value of <typeparamref name="T"/> or an error message.</summary>
public readonly struct Result<T>
{
    private readonly T? _value;

    internal Result(bool isSuccess, T? value, string? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    /// <summary>True when this is a success.</summary>
    public bool IsSuccess { get; }

    /// <summary>True when this is a failure.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The error message on failure; null on success.</summary>
    public string? Error { get; }

    /// <summary>The success value. Throws <see cref="InvalidOperationException"/> if accessed on a failure.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Result is a failure: {Error}");

    /// <summary>Projects the success value; a failure is propagated unchanged.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return IsSuccess ? Result.Success(map(_value!)) : Result.Failure<TOut>(Error!);
    }

    /// <summary>Folds the result to a single value via the matching branch.</summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<string, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(_value!) : onFailure(Error!);
    }
}

/// <summary>Non-generic factory entry points for <see cref="Result{T}"/> (keeps the static factories off
/// the generic type, per CA1000, and lets <c>Result.Success(value)</c> infer the type argument).</summary>
public static class Result
{
    /// <summary>A success carrying <paramref name="value"/>.</summary>
    public static Result<T> Success<T>(T value) => new(true, value, null);

    /// <summary>A failure carrying <paramref name="error"/>.</summary>
    public static Result<T> Failure<T>(string error) => new(false, default, error);
}
