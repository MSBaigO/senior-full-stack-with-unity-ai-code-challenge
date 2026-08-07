using StratusFieldLink.Api.Domain;

namespace StratusFieldLink.Api.Api;

public sealed record SketchSummaryDto(
    string Id,
    string PackageId,
    string CrewId,
    string Discipline,
    double DiameterInches,
    string Material,
    int NodeCount,
    double LengthFeet,
    double ClientLengthFeet,
    double ElevationChangeFeet,
    bool LengthMismatch,
    string Status,
    DateTime CapturedAtUtc,
    DateTime? PublishedAtUtc,
    string ClientVersion);

public sealed record SketchDetailDto(
    SketchSummaryDto Summary,
    IReadOnlyList<SketchNode> Nodes,
    IReadOnlyList<StatusChange> History,
    string? ReviewNotes);

public sealed record StationCountDto(string Key, int Count);

public sealed record DashboardDto(
    int TotalSketches,
    IReadOnlyList<StationCountDto> CountsByStatus,
    IReadOnlyList<StationCountDto> CountsByCrew,
    int NeedsReviewCount,
    int StaleReviewCount,
    int LengthMismatchCount,
    double TotalPublishedFeet,
    IReadOnlyList<SketchSummaryDto> OldestAwaitingReview);

public sealed record TokenRequestDto(string DeviceCode, string CrewId);

public sealed record TokenResponseDto(string AccessToken, string CrewId, DateTime ExpiresAtUtc);

public static class DtoMapper
{
    public const double LengthMismatchToleranceFeet = 0.5;

    public static SketchSummaryDto ToSummary(this FieldSketch s)
    {
        var serverLength = s.LengthFeet;
        return new SketchSummaryDto(
            s.Id,
            s.PackageId,
            s.CrewId,
            s.Discipline,
            s.DiameterInches,
            s.Material,
            s.Nodes.Count,
            Math.Round(serverLength, 2),
            Math.Round(s.ClientLengthFeet, 2),
            Math.Round(RunGeometry.ElevationChangeFeet(s.Nodes), 2),
            Math.Abs(serverLength - s.ClientLengthFeet) > LengthMismatchToleranceFeet,
            s.Status.ToString(),
            s.CapturedAtUtc,
            s.PublishedAtUtc,
            s.ClientVersion);
    }

    public static SketchDetailDto ToDetail(this FieldSketch s)
        => new(s.ToSummary(), s.Nodes, s.History, s.ReviewNotes);
}
