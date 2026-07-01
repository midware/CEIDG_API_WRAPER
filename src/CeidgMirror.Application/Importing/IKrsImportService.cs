namespace CeidgMirror.Application.Importing;

public interface IKrsImportService
{
    Task RunKrsImportAsync(CancellationToken cancellationToken = default);
}
