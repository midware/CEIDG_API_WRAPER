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
        var address = TryGetProperty(seatAndAddress, "adres", out var addressElement) ? addressElement : default;
        var seat = TryGetProperty(seatAndAddress, "siedziba", out var seatDetailsElement) ? seatDetailsElement : default;
        var pkdCodesJson = ExtractPkdCodesJson(root, out var mainPkdCode);

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
            ReadString(address, "kraj") ?? ReadString(seat, "kraj"),
            ReadString(seat, "wojewodztwo") ?? ReadString(address, "wojewodztwo"),
            ReadString(seat, "powiat") ?? ReadString(address, "powiat"),
            ReadString(seat, "gmina") ?? ReadString(address, "gmina"),
            ReadString(address, "miejscowosc") ?? ReadString(seat, "miejscowosc") ?? ReadString(address, "poczta"),
            ReadString(address, "ulica"),
            ReadString(address, "nrDomu"),
            ReadString(address, "nrLokalu"),
            ReadString(address, "kodPocztowy"),
            ReadString(seatAndAddress, "adresDoDoreczenElektronicznychWpisanyDoBAE") ?? FindFirstString(root, "adresDoreczenElektronicznych"),
            mainPkdCode,
            pkdCodesJson,
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

    private static string? ExtractPkdCodesJson(JsonElement root, out string? mainPkdCode)
    {
        mainPkdCode = null;
        var entries = new List<Dictionary<string, object?>>();
        if (!FindFirstElementByName(root, "przedmiotDzialalnosci", out var activity))
        {
            return null;
        }

        AddPkdEntries(activity, "przedmiotPrzewazajacejDzialalnosci", isMain: true, entries, ref mainPkdCode);
        AddPkdEntries(activity, "przedmiotPozostalejDzialalnosci", isMain: false, entries, ref mainPkdCode);
        mainPkdCode ??= entries.Count > 0 ? entries[0]["kod"] as string : null;

        return entries.Count == 0 ? null : JsonSerializer.Serialize(entries);
    }

    private static void AddPkdEntries(
        JsonElement activity,
        string propertyName,
        bool isMain,
        List<Dictionary<string, object?>> entries,
        ref string? mainPkdCode)
    {
        if (!TryGetProperty(activity, propertyName, out var group))
        {
            return;
        }

        if (group.ValueKind == JsonValueKind.Object)
        {
            AddPkdEntry(group, isMain, entries, ref mainPkdCode);
            return;
        }

        if (group.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in group.EnumerateArray())
        {
            AddPkdEntry(item, isMain, entries, ref mainPkdCode);
        }
    }

    private static void AddPkdEntry(JsonElement item, bool isMain, List<Dictionary<string, object?>> entries, ref string? mainPkdCode)
    {
        var code = ReadPkdCode(item);
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        if (isMain && string.IsNullOrWhiteSpace(mainPkdCode))
        {
            mainPkdCode = code;
        }

        entries.Add(new Dictionary<string, object?>
        {
            ["kod"] = code,
            ["nazwa"] = ReadString(item, "opis") ?? ReadString(item, "nazwa"),
            ["main"] = isMain
        });
    }

    private static string? ReadPkdCode(JsonElement item)
    {
        var direct = ReadString(item, "kod");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct.Replace(".", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();
        }

        var section = ReadString(item, "kodDzial");
        var classCode = ReadString(item, "kodKlasa");
        var subclass = ReadString(item, "kodPodklasa");
        if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(classCode))
        {
            return null;
        }

        return string.Concat(section, classCode, subclass).Replace(".", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();
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
        if (FindFirstElementByName(element, propertyName, out var value))
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }

        return null;
    }

    private static bool FindFirstElementByName(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }

                if (FindFirstElementByName(property.Value, propertyName, out value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (FindFirstElementByName(item, propertyName, out value))
                {
                    return true;
                }
            }
        }

        value = default;
        return false;
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
