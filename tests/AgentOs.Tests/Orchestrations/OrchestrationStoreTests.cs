using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using AgentOs.Modules.Pipeline.Persistence;
using AgentOs.Web.Orchestrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Orchestrations;

/// <summary>
/// Regression cover for the OrchestrationStore load path. A single corrupt/legacy DefinitionJson row
/// (e.g. one missing the <c>required</c> "id" member) used to throw out of <c>LoadOrSeed</c> and take the
/// whole Orchestration Studio down for the tenant. The store must now skip bad rows and keep the rest, and
/// seed defaults when every row is unreadable.
/// </summary>
public sealed class OrchestrationStoreTests
{
    private const string Tenant = "t1";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static OrchestrationStore BuildStore(IReadOnlyList<OrchestrationRecord> records)
    {
        var repo = Substitute.For<IOrchestrationRepository>();
        repo.ListForTenantAsync(Tenant, Arg.Any<CancellationToken>()).Returns(records);

        var services = new ServiceCollection();
        services.AddScoped(_ => repo);
        var sp = services.BuildServiceProvider();

        return new OrchestrationStore(sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<OrchestrationStore>.Instance);
    }

    private static OrchestrationRecord ValidRecord(string id, string name)
    {
        var graph = new OrchestrationGraph { Id = id, Name = name };
        return new OrchestrationRecord(id, name, null, JsonSerializer.Serialize(graph, Json), DateTimeOffset.UtcNow);
    }

    [Fact]
    public void All_OneCorruptRow_SkipsBadRow_KeepsValidGraphs()
    {
        // "{}" is missing the required "id" member → JsonSerializer.Deserialize<OrchestrationGraph> throws.
        var corrupt = new OrchestrationRecord("bad", "Bad", null, "{}", DateTimeOffset.UtcNow);
        var store = BuildStore([ValidRecord("good", "Good Graph"), corrupt]);

        var all = store.All(Tenant);

        all.Count.ShouldBe(1);
        all[0].Id.ShouldBe("good");
    }

    [Fact]
    public void All_EveryRowCorrupt_FallsBackToSeededDefaults()
    {
        var corrupt = new OrchestrationRecord("bad", "Bad", null, "{ not valid json", DateTimeOffset.UtcNow);
        var store = BuildStore([corrupt]);

        var all = store.All(Tenant);

        // Falls through to SeedDefaults() rather than presenting an empty Studio.
        all.Count.ShouldBeGreaterThan(0);
    }
}
