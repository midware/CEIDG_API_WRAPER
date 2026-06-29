using System.Text.Json;
using CeidgMirror.Application.Importing;

namespace CeidgMirror.Infrastructure.Importing;

public static class CeidgFirmyResponseParser
{
    public static IReadOnlyList<CompanyIndexItem> ParseCompanies(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!TryGetProperty(document.RootElement, "firmy", out var firmy) || firmy.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<CompanyIndexItem>();
        foreach (var firma in firmy.EnumerateArray())
        {
            var owner = TryGetProperty(firma, "wlasciciel", out var ownerElement)
                ? ownerElement
                : default;

            items.Add(new CompanyIndexItem(
                ReadString(firma, "id"),
                ReadString(owner, "nip"),
                ReadString(owner, "regon"),
                ReadString(firma, "link"),
                firma.GetRawText()));
        }

        return items;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        if (!TryGetProperty(element, propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}
