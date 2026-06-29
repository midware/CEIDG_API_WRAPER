namespace CeidgMirror.Contracts;

public sealed record CeidgReportDescriptor(
    string GeneratedReportId,
    string ReportName,
    string? ReportDescription,
    string? ReportParameters,
    string FileType,
    DateTimeOffset? GeneratedOn,
    DateOnly? GeneratedOnOnlyDate,
    string RawJson);
