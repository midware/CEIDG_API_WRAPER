using System.Text.Json;

namespace CeidgMirror.Infrastructure.Importing;

public sealed record CeidgZmianaPage(IReadOnlyList<string> CompanyIds, int? Count);

public static class CeidgZmianaResponseParser
{
    public static IReadOnlyList<string> ParseCompanyIds(string json) => ParsePage(json).CompanyIds;

    public static CeidgZmianaPage ParsePage(string json)
    {
        using var document = JsonDocument.Parse(json);
        var count = document.RootElement.TryGetProperty("count", out var countElement) &&
                    countElement.ValueKind == JsonValueKind.Number &&
                    countElement.TryGetInt32(out var parsedCount)
            ? parsedCount
            : (int?)null;

        if (!document.RootElement.TryGetProperty("identyfikatoryWpisow", out var ids) || ids.ValueKind != JsonValueKind.Array)
        {
            return new CeidgZmianaPage(Array.Empty<string>(), count);
        }

        var result = new List<string>();
        foreach (var id in ids.EnumerateArray())
        {
            if (id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()))
            {
                result.Add(id.GetString()!);
            }
        }

        return new CeidgZmianaPage(result, count);
    }
}
