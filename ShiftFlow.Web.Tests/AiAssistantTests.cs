using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShiftFlow.Application.AI;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using Xunit;

namespace ShiftFlow.Web.Tests;

/// <summary>Covers the three pieces of the AI assistant that can be tested without an LLM: the
/// confirm-token store, the pure attachment-stripping helper the orchestrator's tool-result path
/// uses, and two tool functions against the shared SQLite harness.</summary>
public class AiAssistantTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    // ── PendingActionStore ────────────────────────────────────────────────────

    private static PendingActionStore NewStore(out TestClock clock)
    {
        clock = new TestClock();
        return new PendingActionStore(new MemoryCache(new MemoryCacheOptions { Clock = clock }));
    }

    [Fact]
    public void PendingAction_IsReturnedToItsOwner()
    {
        var store = NewStore(out _);
        var token = store.Create("user-1", "Retire A-1", "Asset.Manage", (_, _) => Task.FromResult<object>(new { ok = true }));

        var action = store.Peek(token, "user-1");
        Assert.NotNull(action);
        Assert.Equal("Retire A-1", action!.Description);
        Assert.Equal("Asset.Manage", action.RequiredPermission);
    }

    [Fact]
    public void PendingAction_IsInvisibleToAnotherUser_AndSurvivesTheirAttempt()
    {
        var store = NewStore(out _);
        var token = store.Create("user-1", "Retire A-1", null, (_, _) => Task.FromResult<object>(new { ok = true }));

        Assert.Null(store.Peek(token, "user-2"));
        Assert.Null(store.Take(token, "user-2"));
        Assert.False(store.Discard(token, "user-2"));

        // Another user's failed attempt must not consume the owner's token.
        Assert.NotNull(store.Take(token, "user-1"));
    }

    [Fact]
    public void PendingAction_IsSingleUse()
    {
        var store = NewStore(out _);
        var token = store.Create("user-1", "Bulk reassign", null, (_, _) => Task.FromResult<object>(new { ok = true }));

        Assert.NotNull(store.Take(token, "user-1"));
        Assert.Null(store.Take(token, "user-1"));
        Assert.Null(store.Peek(token, "user-1"));
    }

    [Fact]
    public void PendingAction_ExpiresAfterTheTtl()
    {
        var store = NewStore(out var clock);
        var token = store.Create("user-1", "Force close WO-1", null, (_, _) => Task.FromResult<object>(new { ok = true }));

        clock.Advance(PendingActionStore.Ttl - TimeSpan.FromSeconds(30));
        Assert.NotNull(store.Peek(token, "user-1"));

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(store.Peek(token, "user-1"));
        Assert.Null(store.Take(token, "user-1"));
    }

    [Fact]
    public void PendingAction_TokensAreDistinctAndUrlSafe()
    {
        var store = NewStore(out _);
        var tokens = Enumerable.Range(0, 50)
            .Select(_ => store.Create("user-1", "x", null, (_, _) => Task.FromResult<object>(new { })))
            .ToList();

        Assert.Equal(50, tokens.Distinct().Count());
        Assert.All(tokens, t => Assert.Equal(32, t.Length));
        Assert.All(tokens, t => Assert.DoesNotContain(t, c => c is '+' or '/' or '='));
    }

    // ── AiToolResultEnvelope.Strip ────────────────────────────────────────────

    [Fact]
    public void Strip_RemovesASingleUiPayload_AndReturnsItAsAnAttachment()
    {
        var table = new AiTableAttachment
        {
            Title = "Assets",
            Columns = [new("assetTag", "Tag")],
            Rows = [new Dictionary<string, object?> { ["assetTag"] = "A-1", ["_url"] = "/Assets/Details/1", ["_status"] = "Working" }],
        };

        var (json, attachments) = AiToolResultEnvelope.Strip(new { totalMatches = 1, ui = table }, WebJson);

        var node = JsonNode.Parse(json)!.AsObject();
        Assert.False(node.ContainsKey("ui"));
        Assert.Equal(1, node["totalMatches"]!.GetValue<int>());

        var attachment = Assert.Single(attachments);
        var ui = ((JsonNode)attachment).AsObject();
        Assert.Equal("table", ui["kind"]!.GetValue<string>());
        // Dictionary keys keep their exact casing — the client's reserved _url/_status must survive.
        var row = ui["rows"]!.AsArray()[0]!.AsObject();
        Assert.Equal("/Assets/Details/1", row["_url"]!.GetValue<string>());
        Assert.Equal("Working", row["_status"]!.GetValue<string>());
    }

    [Fact]
    public void Strip_FlattensAUiArrayIntoSeveralAttachments()
    {
        var payload = new
        {
            id = 7,
            ui = new object[]
            {
                new AiCardsAttachment { Items = [new AiCard { Title = "A-1" }] },
                new AiLinksAttachment { Items = [new AiLinkItem("Open asset", "/Assets/Details/7")] },
            },
        };

        var (json, attachments) = AiToolResultEnvelope.Strip(payload, WebJson);

        Assert.False(JsonNode.Parse(json)!.AsObject().ContainsKey("ui"));
        Assert.Equal(2, attachments.Count);
        Assert.Equal("cards", ((JsonNode)attachments[0]).AsObject()["kind"]!.GetValue<string>());
        Assert.Equal("links", ((JsonNode)attachments[1]).AsObject()["kind"]!.GetValue<string>());
    }

    [Fact]
    public void Strip_LeavesAResultWithNoUiUntouched()
    {
        var (json, attachments) = AiToolResultEnvelope.Strip(new { success = true, workOrderId = 4 }, WebJson);

        Assert.Empty(attachments);
        var node = JsonNode.Parse(json)!.AsObject();
        Assert.True(node["success"]!.GetValue<bool>());
        Assert.Equal(4, node["workOrderId"]!.GetValue<int>());
    }

    [Fact]
    public void Strip_DropsANullUiWithoutProducingAnAttachment()
    {
        // Search tools set ui = null when they found nothing, so the property is present but empty.
        var (json, attachments) = AiToolResultEnvelope.Strip(new { returned = 0, ui = (object?)null }, WebJson);

        Assert.Empty(attachments);
        Assert.False(JsonNode.Parse(json)!.AsObject().ContainsKey("ui"));
    }

    [Fact]
    public void Strip_SerializesTheConfirmAttachmentTheClientContractExpects()
    {
        var confirm = new AiConfirmAttachment
        {
            Token = "abc",
            Title = "Retire asset",
            Summary = "Retire A-1.",
            Items = ["A-1"],
            ActionLabel = "Retire",
            Danger = true,
        };

        var (_, attachments) = AiToolResultEnvelope.Strip(new { pendingConfirmation = true, ui = confirm }, WebJson);

        var ui = ((JsonNode)Assert.Single(attachments)).AsObject();
        Assert.Equal("confirm", ui["kind"]!.GetValue<string>());
        Assert.Equal("abc", ui["token"]!.GetValue<string>());
        Assert.Equal("Retire", ui["actionLabel"]!.GetValue<string>());
        Assert.True(ui["danger"]!.GetValue<bool>());
    }

    // ── Tool functions on SQLite ──────────────────────────────────────────────

    private static AiInsightToolFunctions CreateInsightTools(ApplicationDbContext db, IPendingActionStore pending, string managerId)
    {
        var permissions = new StubPermissionService(managerId);
        return new AiInsightToolFunctions(db, new AssetScopeService(db), permissions,
            OrderTestDb.CreateInspectionOrderService(db, managerId),
            OrderTestDb.CreateMaintenanceOrderService(db),
            OrderTestDb.CreateWorkOrderService(db, managerId),
            pending);
    }

    private static JsonObject AsJson(object result) => JsonSerializer.SerializeToNode(result, WebJson)!.AsObject();

    [Fact]
    public async Task GetDailyBriefing_ReturnsEveryContractedSection()
    {
        var (cs, keepAlive) = OrderTestDb.CreateSharedMemoryDb();
        using var _ = keepAlive;
        var seed = await OrderTestDb.SeedAsync(cs);

        using (var setup = OrderTestDb.CreateContext(cs))
        {
            // One overdue maintenance order, one low-stock part, one contract expiring this month.
            var type = new OrderType { Name = "Direct", Prefix = "MT", IsActive = true, IsDirectFix = true };
            setup.OrderTypes.Add(type);
            setup.SpareParts.Add(new SparePart { Name = "Fuse", StockQuantity = 1, ReorderThreshold = 5, IsActive = true });
            await setup.SaveChangesAsync();

            var order = await OrderTestDb.CreateMaintenanceOrderService(setup)
                .CreateAsync(seed.AssetId, seed.UserId, null, "Overdue one", DateTime.UtcNow.Date.AddDays(-3), seed.ManagerId, type.Id);
            setup.Contracts.Add(new Contract
            {
                VendorId = 1, ContractType = "Service", ContractNumber = "C-1",
                StartDate = DateTime.UtcNow.Date.AddMonths(-1), EndDate = DateTime.UtcNow.Date.AddDays(10),
            });
            await setup.SaveChangesAsync();
            Assert.Equal(OrderStatuses.Open, order.Status);
        }

        using var db = OrderTestDb.CreateContext(cs);
        var pending = new PendingActionStore(new MemoryCache(new MemoryCacheOptions()));
        var result = await CreateInsightTools(db, pending, seed.ManagerId)
            .GetDailyBriefingAsync(seed.ManagerId, CancellationToken.None);

        var json = AsJson(result);
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), json["date"]!.GetValue<string>());
        foreach (var key in new[]
        {
            "overdue", "workOrdersAwaitingReview", "workOrdersAwaitingFixConfirmation",
            "defectiveAssetsWithNoOpenOrder", "lowStockParts", "contractsExpiringWithin30Days",
            "myOpenOrders", "ui",
        })
            Assert.True(json.ContainsKey(key), $"briefing is missing {key}");

        Assert.Single(json["overdue"]!["maintenanceOrders"]!.AsArray());
        Assert.Equal(1, json["overdue"]!["total"]!.GetValue<int>());
        Assert.Single(json["lowStockParts"]!.AsArray());
        Assert.Single(json["contractsExpiringWithin30Days"]!.AsArray());

        // The briefing's own attachments: a cards summary, then the links list.
        var (_, attachments) = AiToolResultEnvelope.Strip(result, WebJson);
        Assert.Equal("cards", ((JsonNode)attachments[0]).AsObject()["kind"]!.GetValue<string>());
        Assert.Contains(attachments, a => ((JsonNode)a).AsObject()["kind"]!.GetValue<string>() == "links");
    }

    [Fact]
    public async Task BulkCreateInspectionOrders_PreviewsFirst_ThenCreatesOnConfirm()
    {
        var (cs, keepAlive) = OrderTestDb.CreateSharedMemoryDb();
        using var _ = keepAlive;
        var seed = await OrderTestDb.SeedAsync(cs);

        int zoneId;
        using (var setup = OrderTestDb.CreateContext(cs))
        {
            setup.OrderTypes.Add(new OrderType
            {
                Name = "Inspection", Prefix = "INS", IsActive = true, IsDirectFix = false, TracksDefectOutcome = true,
            });
            await setup.SaveChangesAsync();
            zoneId = await setup.Assets.Where(a => a.Id == seed.AssetId).Select(a => a.ZoneId).FirstAsync();
        }

        var pending = new PendingActionStore(new MemoryCache(new MemoryCacheOptions()));

        // ── Propose (request 1) ──
        string token;
        using (var db = OrderTestDb.CreateContext(cs))
        {
            var preview = await CreateInsightTools(db, pending, seed.ManagerId)
                .BulkCreateInspectionOrdersAsync(zoneId, null, null, seed.UserId, null, null, seed.ManagerId, CancellationToken.None);

            var json = AsJson(preview);
            Assert.True(json["pendingConfirmation"]!.GetValue<bool>());
            Assert.Equal(1, json["assetCount"]!.GetValue<int>());
            token = json["token"]!.GetValue<string>();

            var (modelJson, attachments) = AiToolResultEnvelope.Strip(preview, WebJson);
            Assert.DoesNotContain("\"ui\"", modelJson);
            var confirm = ((JsonNode)Assert.Single(attachments)).AsObject();
            Assert.Equal("confirm", confirm["kind"]!.GetValue<string>());
            Assert.Equal(token, confirm["token"]!.GetValue<string>());

            // Nothing may exist yet — a preview must not write.
            Assert.Equal(0, await db.InspectionOrders.CountAsync());
        }

        // ── Confirm (request 2: a fresh scope, exactly as the Confirm endpoint runs it) ──
        using (var db = OrderTestDb.CreateContext(cs))
        {
            var action = pending.Take(token, seed.ManagerId);
            Assert.NotNull(action);

            var services = new StubServiceProvider(CreateInsightTools(db, pending, seed.ManagerId));
            var result = await action!.Execute(services, CancellationToken.None);

            var json = AsJson(result);
            Assert.True(json["success"]!.GetValue<bool>());
            Assert.Equal(1, json["assetCount"]!.GetValue<int>());
            Assert.StartsWith("INS-", json["orderNumber"]!.GetValue<string>());
        }

        using (var verify = OrderTestDb.CreateContext(cs))
        {
            var order = await verify.InspectionOrders.Include(o => o.InspectionRun).ThenInclude(r => r!.Items).SingleAsync();
            Assert.Equal(seed.UserId, order.AssignedToUserId);
            Assert.Equal(seed.AssetId, Assert.Single(order.InspectionRun!.Items).AssetId);
        }

        // The token is spent: a replayed confirmation can't create a second order.
        Assert.Null(pending.Take(token, seed.ManagerId));
    }
}

/// <summary>Minimal IServiceProvider for the confirm path — the real one is the confirming HTTP
/// request's scope, which resolves the tool class fresh rather than reusing anything the proposing
/// request captured.</summary>
public sealed class StubServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();
    public StubServiceProvider(params object[] services)
    {
        foreach (var service in services) _services[service.GetType()] = service;
    }
    public object? GetService(Type serviceType) => _services.TryGetValue(serviceType, out var s) ? s : null;
}

/// <summary>Lets the TTL test move time forward instead of sleeping for ten minutes.</summary>
public sealed class TestClock : Microsoft.Extensions.Internal.ISystemClock
{
    public DateTimeOffset UtcNow { get; private set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
