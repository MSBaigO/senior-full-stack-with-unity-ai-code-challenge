using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StratusFieldLink.Api.Domain;
using StratusFieldLink.Api.Infrastructure;
using Xunit;

namespace StratusFieldLink.Api.Tests;

public class PublishEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly InMemorySketchRepository _repo = new();
    private readonly HttpClient _client;

    public PublishEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddSingleton<ISketchRepository>(_repo))).CreateClient();
    }

    private static object Payload(string id, double toX = 24, object? extra = null) => new Dictionary<string, object?>
    {
        ["id"] = id,
        ["packageId"] = "PKG-CHW-L02",
        ["crewId"] = "CREW-04",
        ["discipline"] = "Piping",
        ["diameterInches"] = 4,
        ["material"] = "Carbon Steel",
        ["nodes"] = new[] { new { x = 0d, y = 10d, z = 0d }, new { x = toX, y = 10d, z = 0d } },
        ["capturedAtUtc"] = "2026-10-20T08:00:00Z",
        ["clientLengthFeet"] = toX,
        ["clientVersion"] = "field-1.4.2",
        ["status"] = (extra as string)
    };

    private static async Task<JsonElement> Json(HttpResponseMessage r)
        => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Post_NewSketch_Returns201_AndShowsUpInTheListAndDashboard()
    {
        var response = await _client.PostAsJsonAsync("/api/sketches", Payload("SK-NEW-1"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("Published", body.GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("revision").GetInt32());

        var list = await Json(await _client.GetAsync("/api/sketches"));
        Assert.Contains(list.EnumerateArray(), s => s.GetProperty("id").GetString() == "SK-NEW-1");
        var dash = await Json(await _client.GetAsync("/api/dashboard"));
        Assert.Equal(1, dash.GetProperty("totalSketches").GetInt32());
    }

    [Fact]
    public async Task Post_ClientCannotChooseTheStatus()
    {
        var response = await _client.PostAsJsonAsync("/api/sketches", Payload("SK-NEW-2", extra: "Approved"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(SketchStatus.Published, _repo.Find("SK-NEW-2")!.Status);
        Assert.NotNull(_repo.Find("SK-NEW-2")!.PublishedAtUtc);
    }

    [Fact]
    public async Task Post_SameIdSameContent_Returns200_AndLeavesTheSketchAlone()
    {
        await _client.PostAsJsonAsync("/api/sketches", Payload("SK-DUP"));
        _repo.Find("SK-DUP")!.Status = SketchStatus.Approved;

        var again = await _client.PostAsJsonAsync("/api/sketches", Payload("SK-DUP"));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(SketchStatus.Approved, _repo.Find("SK-DUP")!.Status);
        Assert.Single(_repo.All());
    }

    [Fact]
    public async Task Post_SameIdDifferentContent_Returns409_AndKeepsTheOriginal()
    {
        await _client.PostAsJsonAsync("/api/sketches", Payload("SK-CLASH", toX: 24));

        var clash = await _client.PostAsJsonAsync("/api/sketches", Payload("SK-CLASH", toX: 40));

        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Contains("SK-CLASH", (await Json(clash)).GetProperty("error").GetString());
        Assert.Equal(24, _repo.Find("SK-CLASH")!.Nodes[1].X);
    }

    [Fact]
    public async Task Post_SameIdButDifferentClientLength_IsNotADuplicate()
    {
        await _client.PostAsJsonAsync("/api/sketches", Payload("SK-LEN"));
        var changed = (Dictionary<string, object?>)Payload("SK-LEN");
        changed["clientLengthFeet"] = 99d;

        var response = await _client.PostAsJsonAsync("/api/sketches", changed);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(24, _repo.Find("SK-LEN")!.ClientLengthFeet);
    }

    [Fact]
    public async Task Post_InvalidSketch_Returns400_WithFieldFailures()
    {
        var payload = (Dictionary<string, object?>)Payload("SK-BAD");
        payload["nodes"] = new[] { new { x = 0d, y = 10d, z = 0d } };

        var response = await _client.PostAsJsonAsync("/api/sketches", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var failures = (await Json(response)).GetProperty("failures").EnumerateArray();
        Assert.Contains(failures, f => f.GetProperty("field").GetString() == "nodes");
        Assert.Null(_repo.Find("SK-BAD"));
    }

    [Fact]
    public async Task Post_MissingCapturedAt_Returns400()
    {
        var payload = (Dictionary<string, object?>)Payload("SK-NOCAP");
        payload.Remove("capturedAtUtc");

        var response = await _client.PostAsJsonAsync("/api/sketches", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var failures = (await Json(response)).GetProperty("failures").EnumerateArray();
        Assert.Contains(failures, f => f.GetProperty("field").GetString() == "capturedAtUtc");
    }

    [Fact]
    public async Task Post_ReworkedRejectedSketch_IsRepublishedAsANewRevision()
    {
        await _client.PostAsJsonAsync("/api/sketches", Payload("SK-REWORK", toX: 24));
        _repo.Find("SK-REWORK")!.Status = SketchStatus.Rejected;

        var response = await _client.PostAsJsonAsync("/api/sketches", Payload("SK-REWORK", toX: 30));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response);
        Assert.Equal(2, body.GetProperty("revision").GetInt32());
        Assert.Equal("Published", body.GetProperty("status").GetString());
        var stored = _repo.Find("SK-REWORK")!;
        Assert.Equal(30, stored.Nodes[1].X);
        var last = stored.History[^1];
        Assert.Equal((SketchStatus.Rejected, SketchStatus.Published), (last.From, last.To));
    }
}
