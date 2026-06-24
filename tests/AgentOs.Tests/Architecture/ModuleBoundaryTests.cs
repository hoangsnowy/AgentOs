// Architecture guardrail: the Web host must depend on the Pipeline module ONLY through its public seam —
// the Domain graph DTOs + the IGraphExecutor facade — never the module-internal GraphExecution namespace
// (the concrete GraphExecutor, MAF wiring, etc.). This is the durable defense that keeps the boundary
// from re-leaking after PR5 moved the DTOs to Domain: if someone re-adds a `using` and references a
// GraphExecution type in a Web type's signature, this fails. Reflection over member signatures (fields,
// properties, ctor/method parameters + returns, unwrapping generics + arrays).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AgentOs.Web.Orchestrations;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Architecture;

public sealed class ModuleBoundaryTests
{
    private const string InternalGraphNamespace = "AgentOs.Modules.Pipeline.GraphExecution";

    [Fact]
    public void WebHost_DependsOnGraphFacade_NotPipelineGraphExecutionInternals()
    {
        var webAssembly = typeof(GraphRunnerService).Assembly;

        var offenders = (
            from type in SafeGetTypes(webAssembly)
            from sig in SignatureTypes(type)
            from t in Flatten(sig.Type)
            where t.Namespace == InternalGraphNamespace
            select $"{type.FullName}.{sig.Member} -> {t.Name}")
            .Distinct()
            .ToList();

        offenders.ShouldBeEmpty(
            $"Web types must reach the executor via AgentOs.Domain.Pipeline.Graph (DTOs + IGraphExecutor), "
            + $"never the module-internal {InternalGraphNamespace}. Offenders:\n  " + string.Join("\n  ", offenders));
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static IEnumerable<(string Member, Type Type)> SignatureTypes(Type type)
    {
        foreach (var f in type.GetFields(AllMembers)) { yield return (f.Name, f.FieldType); }
        foreach (var p in type.GetProperties(AllMembers)) { yield return (p.Name, p.PropertyType); }
        foreach (var c in type.GetConstructors(AllMembers))
        {
            foreach (var pi in c.GetParameters()) { yield return (".ctor", pi.ParameterType); }
        }
        foreach (var m in type.GetMethods(AllMembers))
        {
            yield return (m.Name, m.ReturnType);
            foreach (var pi in m.GetParameters()) { yield return (m.Name, pi.ParameterType); }
        }
    }

    private static IEnumerable<Type> Flatten(Type t)
    {
        yield return t;
        if (t.IsGenericType)
        {
            foreach (var ga in t.GetGenericArguments())
            {
                foreach (var inner in Flatten(ga)) { yield return inner; }
            }
        }
        if (t.HasElementType && t.GetElementType() is { } element)
        {
            foreach (var inner in Flatten(element)) { yield return inner; }
        }
    }
}
