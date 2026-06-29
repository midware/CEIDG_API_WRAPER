using CeidgMirror.Contracts;

namespace CeidgMirror.Application.Importing;

public interface ICompanyRecordStore
{
    Task<Guid> StartImportRunAsync(string importKind, CancellationToken cancellationToken = default);

    Task CompleteImportRunAsync(
        Guid importRunId,
        string status,
        object details,
        CancellationToken cancellationToken = default);

    Task UpsertCompanyAsync(
        CompanyIndexItem? indexItem,
        CeidgRawResponse detailResponse,
        Guid importRunId,
        CancellationToken cancellationToken = default);

    Task UpsertReportPayloadAsync(
        CeidgReportDescriptor report,
        CeidgRawResponse payloadResponse,
        Guid importRunId,
        CancellationToken cancellationToken = default);
}
