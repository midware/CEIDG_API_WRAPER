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

    Task<ImportCheckpoint?> GetCheckpointAsync(
        string checkpointKey,
        CancellationToken cancellationToken = default);

    Task SaveCheckpointAsync(
        ImportCheckpoint checkpoint,
        object details,
        CancellationToken cancellationToken = default);

    Task<bool> CompanyExistsAsync(
        string ceidgId,
        CancellationToken cancellationToken = default);

    Task UpsertCompanyAsync(
        CompanyIndexItem? indexItem,
        CeidgRawResponse detailResponse,
        Guid importRunId,
        CancellationToken cancellationToken = default);

    Task UpsertKrsCompanyAsync(
        KrsCompanyRecord record,
        Guid importRunId,
        CancellationToken cancellationToken = default);

    Task UpsertReportPayloadAsync(
        CeidgReportDescriptor report,
        CeidgRawResponse payloadResponse,
        Guid importRunId,
        CancellationToken cancellationToken = default);
}