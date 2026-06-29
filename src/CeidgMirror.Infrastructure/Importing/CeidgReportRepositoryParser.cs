using System.Text.Json;
using CeidgMirror.Contracts;

namespace CeidgMirror.Infrastructure.Importing;

public static class CeidgReportRepositoryParser
{
    public static IReadOnlyList<CeidgReportDescriptor> ParseReports(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<CeidgReportDescriptor>();
        }

        var reports = new List<CeidgReportDescriptor>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = ReadString(item, "generatedReportId");
            var name = ReadString(item, "reportName");
            var fileType = ReadString(item, "fileType");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(fileType))
            {
                continue;
            }

            reports.Add(new CeidgReportDescriptor(
                id,
                name,
                ReadString(item, "reportDescription"),
                ReadString(item, "reportParameters"),
                fileType,
                ReadDateTimeOffset(item, "generatedOn"),
                ReadDateOnly(item, "generatedOnOnlyDate"),
                item.GetRawText()));
        }

        return reports;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static DateOnly? ReadDateOnly(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return DateOnly.TryParse(value, out var parsed) ? parsed : null;
    }
}
