using StratusFieldLink.Api.Domain;
using StratusFieldLink.Api.Infrastructure;

namespace StratusFieldLink.Api.Api;

public static class SketchEndpoints
{
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

        // ---------------------------------------------------------------------
        // TODO(candidate): the Unity field client needs to publish a sketch here.
        //
        //   POST /api/sketches
        //
        // Decide the contract, the validation, the status code, and what happens
        // when the same client-generated id is published twice (the field app
        // retries when it comes back into signal). SketchValidator is already
        // written and tested; wire it up.
        //
        // Whatever you accept has to show up in GET /api/sketches and on the
        // dashboard without a restart.
        // ---------------------------------------------------------------------

        return app;
    }
}
