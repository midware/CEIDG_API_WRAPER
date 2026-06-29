using System.Globalization;
using System.Text;
using CeidgMirror.Contracts;

namespace CeidgMirror.Infrastructure.Ceidg;

public static class CeidgQueryBuilder
{
    public static Uri BuildCompaniesUri(Uri baseUrl, CeidgFirmySearchRequest request)
    {
        var builder = new QueryStringBuilder();
        builder.AddMany("nip", request.Nip);
        builder.AddMany("regon", request.Regon);
        builder.AddMany("nazwa", request.CompanyName);
        builder.AddMany("miasto", request.City);
        builder.AddMany("wojewodztwo", request.Voivodeship);
        builder.AddMany("pkd", request.Pkd);
        builder.AddMany("status", request.Status.Select(ToApiValue));
        builder.Add("dataod", request.StartedFrom);
        builder.Add("datado", request.StartedTo);
        builder.Add("page", request.Page.ToString(CultureInfo.InvariantCulture));
        builder.Add("limit", request.Limit.ToString(CultureInfo.InvariantCulture));

        return BuildUri(baseUrl, "firmy", builder.ToString());
    }

    public static Uri BuildCompanyDetailsUri(Uri baseUrl, CeidgCompanyDetailRequest request)
    {
        request.Validate();

        var builder = new QueryStringBuilder();
        builder.Add("nip", request.Nip);
        builder.Add("regon", request.Regon);

        return BuildUri(baseUrl, "firma", builder.ToString());
    }

    public static Uri BuildCompanyByIdUri(Uri baseUrl, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return BuildUri(baseUrl, $"firma/{Uri.EscapeDataString(id)}", null);
    }

    public static Uri BuildChangesUri(Uri baseUrl, DateOnly from, DateOnly? to, int page, int limit)
    {
        var builder = new QueryStringBuilder();
        builder.Add("dataod", from);
        builder.Add("datado", to);
        builder.Add("page", page.ToString(CultureInfo.InvariantCulture));
        builder.Add("limit", limit.ToString(CultureInfo.InvariantCulture));

        return BuildUri(baseUrl, "zmiana", builder.ToString());
    }

    private static Uri BuildUri(Uri baseUrl, string path, string? query)
    {
        var root = baseUrl.ToString().TrimEnd('/');
        var url = $"{root}/{path.TrimStart('/')}";
        return string.IsNullOrWhiteSpace(query) ? new Uri(url) : new Uri($"{url}?{query}");
    }

    private static string ToApiValue(CeidgCompanyStatus status) =>
        status switch
        {
            CeidgCompanyStatus.Aktywny => "AKTYWNY",
            CeidgCompanyStatus.Wykreslony => "WYKRESLONY",
            CeidgCompanyStatus.Zawieszony => "ZAWIESZONY",
            CeidgCompanyStatus.OczekujeNaRozpoczecieDzialalnosci => "OCZEKUJE_NA_ROZPOCZECIE_DZIALALNOSCI",
            CeidgCompanyStatus.WylacznieWFormieSpolki => "WYLACZNIE_W_FORMIE_SPOLKI",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported CEIDG company status.")
        };

    private sealed class QueryStringBuilder
    {
        private readonly StringBuilder _builder = new();

        public void AddMany(string name, IEnumerable<string> values)
        {
            foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
            {
                Add(name, value);
            }
        }

        public void Add(string name, DateOnly? value)
        {
            if (value is not null)
            {
                Add(name, value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
        }

        public void Add(string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (_builder.Length > 0)
            {
                _builder.Append('&');
            }

            _builder
                .Append(Uri.EscapeDataString(name))
                .Append('=')
                .Append(Uri.EscapeDataString(value));
        }

        public override string ToString() => _builder.ToString();
    }
}
