using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CeidgMirror.Infrastructure.Normalization;

public static partial class CompanyDataNormalizer
{
    private static readonly CultureInfo PolishCulture = CultureInfo.GetCultureInfo("pl-PL");

    private static readonly IReadOnlyDictionary<string, string> Voivodeships = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["DOLNOSLASKIE"] = "Dolnośląskie",
        ["KUJAWSKO-POMORSKIE"] = "Kujawsko-Pomorskie",
        ["LUBELSKIE"] = "Lubelskie",
        ["LUBUSKIE"] = "Lubuskie",
        ["LODZKIE"] = "Łódzkie",
        ["MALOPOLSKIE"] = "Małopolskie",
        ["MAZOWIECKIE"] = "Mazowieckie",
        ["OPOLSKIE"] = "Opolskie",
        ["PODKARPACKIE"] = "Podkarpackie",
        ["PODLASKIE"] = "Podlaskie",
        ["POMORSKIE"] = "Pomorskie",
        ["SLASKIE"] = "Śląskie",
        ["SWIETOKRZYSKIE"] = "Świętokrzyskie",
        ["WARMINSKO-MAZURSKIE"] = "Warmińsko-Mazurskie",
        ["WIELKOPOLSKIE"] = "Wielkopolskie",
        ["ZACHODNIOPOMORSKIE"] = "Zachodniopomorskie"
    };

    public static string? CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = WhitespaceRegex().Replace(value.Trim(), " ");
        return cleaned.Length == 0 ? null : cleaned;
    }

    public static string? NormalizePersonName(string? value) => ToPolishTitleCase(value);

    public static string? NormalizePlaceName(string? value) => ToPolishTitleCase(value);

    public static string? NormalizeStreet(string? value)
    {
        var normalized = ToPolishTitleCase(value);
        if (normalized is null)
        {
            return null;
        }

        return StreetPrefixRegex().Replace(normalized, match => match.Value.ToLower(PolishCulture));
    }

    public static string? NormalizeVoivodeship(string? value)
    {
        var cleaned = CleanText(value);
        if (cleaned is null)
        {
            return null;
        }

        var key = RemoveDiacritics(cleaned).ToUpperInvariant();
        return Voivodeships.TryGetValue(key, out var canonical) ? canonical : ToPolishTitleCase(cleaned);
    }

    public static string? NormalizeEmailList(string? value)
    {
        var cleaned = CleanText(value);
        if (cleaned is null)
        {
            return null;
        }

        var emails = EmailRegex()
            .Matches(cleaned)
            .Select(match => match.Value.Trim().TrimEnd('.').ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return emails.Length == 0 ? null : string.Join(", ", emails);
    }

    public static string? NormalizeWebsiteList(string? value)
    {
        var cleaned = CleanText(value);
        if (cleaned is null)
        {
            return null;
        }

        var websites = Regex.Split(cleaned, @"[\s,;|]+")
            .Select(NormalizeWebsite)
            .Where(item => item is not null)
            .Select(item => item!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return websites.Length == 0 ? null : string.Join(", ", websites);
    }

    public static string? NormalizePhoneList(string? value)
    {
        var cleaned = CleanText(value);
        if (cleaned is null)
        {
            return null;
        }

        var phones = new List<string>();
        foreach (var segment in PhoneSeparatorRegex().Split(cleaned))
        {
            AddPhoneSegment(segment, phones);
        }

        return phones.Count == 0 ? null : string.Join(", ", phones.Distinct(StringComparer.Ordinal).ToArray());
    }

    public static string? NormalizeDigits(string? value, int? padLeft = null)
    {
        var cleaned = CleanText(value);
        if (cleaned is null)
        {
            return null;
        }

        var digits = new string(cleaned.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return null;
        }

        return padLeft is { } width ? digits.PadLeft(width, '0') : digits;
    }

    public static string? NormalizePkdCode(string? value)
    {
        var cleaned = CleanText(value);
        if (cleaned is null)
        {
            return null;
        }

        var code = Regex.Replace(cleaned, @"[^0-9A-Za-z]", string.Empty).ToUpperInvariant();
        return code.Length == 0 ? null : code;
    }

    public static string? NormalizePostalCode(string? value)
    {
        var digits = NormalizeDigits(value);
        return digits?.Length == 5 ? digits.Insert(2, "-") : CleanText(value);
    }

    public static string? NormalizeStatus(string? value)
    {
        var cleaned = CleanText(value);
        return cleaned?.ToUpper(PolishCulture);
    }

    public static string? NormalizeLegalForm(string? value)
    {
        var normalized = ToPolishTitleCase(value);
        if (normalized is null)
        {
            return null;
        }

        return SmallLegalWordsRegex().Replace(normalized, match => match.Value.ToLower(PolishCulture));
    }

    private static string? NormalizeWebsite(string value)
    {
        var cleaned = value.Trim().TrimEnd('.', '/', '\\');
        if (cleaned.Length == 0 || !cleaned.Contains('.', StringComparison.Ordinal))
        {
            return null;
        }

        cleaned = cleaned.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!cleaned.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !cleaned.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = "https://" + cleaned;
        }

        return cleaned.ToLowerInvariant();
    }

    private static string? ToPolishTitleCase(string? value)
    {
        var cleaned = CleanText(value);
        if (cleaned is null)
        {
            return null;
        }

        var lowered = cleaned.ToLower(PolishCulture);
        var titled = PolishCulture.TextInfo.ToTitleCase(lowered);
        return RomanNumeralRegex().Replace(titled, match => match.Value.ToUpperInvariant());
    }

    private static void AddPhoneSegment(string segment, List<string> phones)
    {
        var digits = new string(segment.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return;
        }

        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }

        if (digits.Length == 9)
        {
            phones.Add("+48" + digits);
            return;
        }

        if (digits.Length == 11 && digits.StartsWith("48", StringComparison.Ordinal))
        {
            phones.Add("+" + digits);
            return;
        }

        if (digits.Length > 11 && digits.StartsWith("48", StringComparison.Ordinal) && (digits.Length - 2) % 9 == 0)
        {
            AddPolishChunks(digits[2..], phones);
            return;
        }

        if (digits.Length > 9 && digits.Length % 9 == 0)
        {
            AddPolishChunks(digits, phones);
            return;
        }

        if (digits.Length > 9)
        {
            phones.Add("+" + digits);
        }
    }

    private static void AddPolishChunks(string digits, List<string> phones)
    {
        for (var index = 0; index + 9 <= digits.Length; index += 9)
        {
            phones.Add("+48" + digits.Substring(index, 9));
        }
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"[,;|/]+")]
    private static partial Regex PhoneSeparatorRegex();

    [GeneratedRegex(@"^(Ul|Al|Pl|Os|Bulw|Rondo)\.", RegexOptions.IgnoreCase)]
    private static partial Regex StreetPrefixRegex();

    [GeneratedRegex(@"\b[IVXLCDM]{2,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanNumeralRegex();

    [GeneratedRegex(@"\b(Z|W|I|Oraz|O|Na|Pod|Przy)\b")]
    private static partial Regex SmallLegalWordsRegex();
}
