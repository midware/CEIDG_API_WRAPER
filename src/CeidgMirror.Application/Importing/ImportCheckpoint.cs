namespace CeidgMirror.Application.Importing;

public sealed record ImportCheckpoint(
    string CheckpointKey,
    string ImportKind,
    DateOnly ChangesFrom,
    DateOnly? ChangesTo,
    int NextPage,
    int NextItemIndex,
    long ImportedCount,
    long SkippedCount,
    long FailedCount,
    bool Completed,
    string? LastCompanyId);