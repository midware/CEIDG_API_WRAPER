using System.Text.Json;
using System.Text.RegularExpressions;

namespace CeidgMirror.Infrastructure.Krs;

public static partial class KrsBulletinParser
{
    public static IReadOnlyList<string> ParseKrsNumbers(string json)
    {
        using var document = JsonDocument.Parse(json);
        var results = new SortedSet<string>(StringComparer.Ordinal);
        Collect(document.RootElement, results);
        return results.ToArray();
    }

    private static void Collect(JsonElement element, SortedSet<string> results)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                AddMatches(element.GetString(), results);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AddMatches(property.Name, results);
                    Collect(property.Value, results);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, results);
                }
                break;
            case JsonValueKind.Number:
                AddMatches(element.ToString(), results);
                break;
        }
    }

    private static void AddMatches(string? value, SortedSet<string> results)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (ShortKrsNumberRegex().IsMatch(trimmed))
        {
            results.Add(trimmed.PadLeft(10, '0'));
            return;
        }

        foreach (Match match in LongKrsNumberRegex().Matches(value))
        {
            results.Add(match.Value.PadLeft(10, '0'));
        }
    }

    [GeneratedRegex(@"^\d{1,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex ShortKrsNumberRegex();

    [GeneratedRegex(@"(?<!\d)\d{10}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex LongKrsNumberRegex();
}
