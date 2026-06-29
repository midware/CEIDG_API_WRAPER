using System.Text.Json;

namespace CeidgMirror.Infrastructure.Importing;

public static class CeidgZmianaResponseParser
{
    public static IReadOnlyList<string> ParseCompanyIds(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("identyfikatoryWpisow", out var ids) || ids.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var id in ids.EnumerateArray())
        {
            if (id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()))
            {
                result.Add(id.GetString()!);
            }
        }

        return result;
    }
}
