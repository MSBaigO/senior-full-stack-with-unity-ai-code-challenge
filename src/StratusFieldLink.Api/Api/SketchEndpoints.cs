using StratusFieldLink.Api.Domain;
using StratusFieldLink.Api.Infrastructure;

namespace StratusFieldLink.Api.Api;

public static class SketchEndpoints
{
    private static readonly object PublishLock = new();
    private const double CoordinateTolerance = 1e-3;

    private static bool SameContent(FieldSketch a, FieldSketch b)
        => string.Equals(a.PackageId, b.PackageId, StringComparison.Ordinal)
           && string.Equals(a.CrewId, b.CrewId, StringComparison.Ordinal)
           && string.Equals(a.Discipline, b.Discipline, StringComparison.Ordinal)
           && string.Equals(a.Material, b.Material, StringComparison.Ordinal)
           && Math.Abs(a.DiameterInches - b.DiameterInches) < CoordinateTolerance
           && a.Nodes.Count == b.Nodes.Count
           && a.Nodes.Zip(b.Nodes).All(p =>
               Math.Abs(p.First.X - p.Second.X) < CoordinateTolerance
               && Math.Abs(p.First.Y - p.Second.Y) < CoordinateTolerance
               && Math.Abs(p.First.Z - p.Second.Z) < CoordinateTolerance);

    public static IEndpointRouteBuilder MapSketchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));

        app.MapGet("/api/sketches", (
            ISketchRepository repo,
            string? status,
            string? packageId,
            string? crewId,
            int? limit) =>
        {
            IEnumerable<FieldSketch> query = repo.All();

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<SketchStatus>(status, ignoreCase: true, out var parsed))
                {
                    return Results.BadRequest(new { error = $"Unknown status '{status}'." });
                }

                query = query.Where(s => s.Status == parsed);
            }

            if (!string.IsNullOrWhiteSpace(packageId))
            {
                query = query.Where(s => string.Equals(s.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(crewId))
            {
                query = query.Where(s => string.Equals(s.CrewId, crewId, StringComparison.OrdinalIgnoreCase));
            }

            var results = query
                .OrderByDescending(s => s.PublishedAtUtc ?? s.CapturedAtUtc)
                .Take(limit is > 0 and <= 500 ? limit.Value : 100)
                .Select(s => s.ToSummary())
                .ToList();

            return Results.Ok(results);
        });

        app.MapGet("/api/sketches/{id}", (ISketchRepository repo, string id) =>
        {
            var sketch = repo.Find(id);
            return sketch is null
                ? Results.NotFound(new { error = $"No sketch '{id}'." })
                : Results.Ok(sketch.ToDetail());
        });

        // POST /api/sketches: 201 new (or reworked) revision, 200 identical duplicate,
        // 400 invalid, 409 same id with different content.
        app.MapPost("/api/sketches", (ISketchRepository repo, PublishSketchRequest request) =>
        {
            var now = DateTime.UtcNow;
            var sketch = new FieldSketch
            {
                Id = request.Id ?? "",
                PackageId = request.PackageId ?? "",
                CrewId = request.CrewId ?? "",
                Discipline = request.Discipline ?? "",
                DiameterInches = request.DiameterInches,
                Material = request.Material ?? "",
                Nodes = request.Nodes ?? new List<SketchNode>(),
                CapturedAtUtc = request.CapturedAtUtc ?? default,
                ClientLengthFeet = request.ClientLengthFeet,
                ClientVersion = request.ClientVersion ?? "",
                Status = SketchStatus.Published,
                PublishedAtUtc = now,
                History = new List<StatusChange>
                {
                    new() { From = SketchStatus.Draft, To = SketchStatus.Published, AtUtc = now, Actor = request.CrewId ?? "" }
                }
            };

            var validation = SketchValidator.Validate(sketch);
            if (request.CapturedAtUtc is null)
            {
                validation.Add("capturedAtUtc", "A capture time is required.");
            }

            if (!validation.IsValid)
            {
                return Results.BadRequest(new { error = "Sketch failed validation.", failures = validation.Failures });
            }

            lock (PublishLock)
            {
                var existing = repo.Find(sketch.Id);
                if (existing is null)
                {
                    repo.Upsert(sketch);
                    return Results.Created($"/api/sketches/{sketch.Id}", sketch.ToSummary());
                }

                if (SameContent(existing, sketch))
                {
                    return Results.Ok(existing.ToSummary());
                }

                if (existing.Status == SketchStatus.Rejected)
                {
                    sketch.Revision = existing.Revision + 1;
                    sketch.ReviewNotes = existing.ReviewNotes;
                    sketch.History = existing.History.Concat(new[]
                    {
                        new StatusChange { From = SketchStatus.Rejected, To = SketchStatus.Published, AtUtc = now, Actor = sketch.CrewId }
                    }).ToList();
                    repo.Upsert(sketch);
                    return Results.Created($"/api/sketches/{sketch.Id}", sketch.ToSummary());
                }

                return Results.Conflict(new
                {
                    error = $"Sketch '{sketch.Id}' was already published with different content (status {existing.Status}). Publish changes under a new id."
                });
            }
        });

        return app;
    }
}
