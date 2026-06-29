namespace CeidgMirror.Application.Importing;

public interface ICeidgImportService
{
    Task RunInitialImportAsync(CancellationToken cancellationToken = default);
}
