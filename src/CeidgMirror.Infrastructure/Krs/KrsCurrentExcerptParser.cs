using System.Globalization;
using System.Text.Json;
using CeidgMirror.Application.Importing;
using CeidgMirror.Contracts;

namespace CeidgMirror.Infrastructure.Krs;

public static class KrsCurrentExcerptParser
{
    private static readonly string[] DateFormats = ["dd.MM.yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "yyyyMMdd"];

    public static KrsCompanyRecord Parse(CeidgRawResponse response)
    {
        using var document = JsonDocument.Parse(response.Content);
        var root = document.RootElement;
        var odpis = TryGetProperty(root, "odpis", out var odpisElement) ? odpisElement : root;
        var header = TryGetProperty(odpis, "naglowekA", out var headerElement) ? headerElement : default;
        var data = TryGetProperty(odpis, "dane", out var dataElement) ? dataElement : default;
        var section1 = TryGetProperty(data, "dzial1", out var section1Element) ? section1Element : default;
        var subject = TryGetProperty(section1, "danePodmiotu", out var subjectElement) ? subjectElement : default;
        var identifiers = TryGetProperty(subject, "identyfikatory", out var identifiersElement) ? identifiersElement : default;
        var seatAndAddress = TryGetProperty(section1, "siedzibaIAdres", out var seatElement) ? seatElement : default;

        var krsNumber = ReadString(header, "numerKRS") ?? FindFirstString(root, "numerKRS");
        if (string.IsNullOrWhiteSpace(krsNumber))
        {
            throw new InvalidOperationException("KRS current excerpt does not contain naglowekA.numerKRS.");
        }

        var registerType = ReadString(header, "rejestr") ?? "RejP";
        var representatives = ReadRawByAnyName(root, "organReprezentacji", "reprezentacja", "sposobReprezentacji");

        return new KrsCompanyRecord(
            NormalizeKrs(krsNumber),
            registerType,
            ReadString(subject, "formaPrawna"),
            ReadString(header, "oznaczenieSaduDokonujacegoOstatniegoWpisu"),
            ReadDate(header, "dataRejestracjiWKRS"),
            ReadDate(header, "dataOstatniegoWpisu"),
            ReadString(header, "stanPozycji") ?? ReadString(header, "status"),
            ReadString(identifiers, "nip"),
            ReadString(identifiers, "regon"),
            ReadString(subject, "nazwa") ?? FindFirstString(root, "nazwa"),
            seatAndAddress.ValueKind is JsonValueKind.Object ? seatAndAddress.GetRawText() : null,
            representatives,
            response.Content,
            response.RequestUri,
            response.FetchedAtUtc);
    }

    private static string NormalizeKrs(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.PadLeft(10, '0');
    }

    private static DateOnly? ReadDate(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : DateOnly.TryParse(value, CultureInfo.GetCultureInfo("pl-PL"), DateTimeStyles.None, out parsed) ? parsed : null;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? FindFirstString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
                }

                var nested = FindFirstString(property.Value, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindFirstString(item, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? ReadRawByAnyName(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return property.Value.GetRawText();
                }

                var nested = ReadRawByAnyName(property.Value, names);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = ReadRawByAnyName(item, names);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
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
