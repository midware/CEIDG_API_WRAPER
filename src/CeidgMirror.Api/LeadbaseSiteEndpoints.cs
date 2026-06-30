using System.Collections.Concurrent;

namespace CeidgMirror.Api;

public static class LeadbaseSiteEndpoints
{
    private const int AnonymousDemoLimit = 2;
    private static readonly ConcurrentDictionary<string, int> AnonymousDemoUsage = new(StringComparer.Ordinal);

    public static WebApplication MapLeadbaseSite(this WebApplication app)
    {
        app.MapGet("/", () => Results.Content(HomeHtml, "text/html; charset=utf-8"))
            .ExcludeFromDescription();

        app.MapGet("/docs", () => Results.Redirect("/swagger"))
            .ExcludeFromDescription();

        app.MapGet("/app", () => Results.Content(HomeHtml.Replace("<body>", "<body data-scroll-target=\"dashboard\">"), "text/html; charset=utf-8"))
            .ExcludeFromDescription();

        app.MapGet("/demo/companies", (HttpContext context, string? name, string? city, string? mainPkdCode, string? columns) =>
        {
            var key = GetAnonymousDemoKey(context);
            var used = AnonymousDemoUsage.AddOrUpdate(key, 1, (_, current) => current + 1);
            if (used > AnonymousDemoLimit)
            {
                return Results.Json(new { error = "Demo limit reached.", registrationRequired = true, limit = AnonymousDemoLimit }, statusCode: StatusCodes.Status403Forbidden);
            }

            var selectedColumns = ResolveDemoColumns(columns);
            var rows = DemoCompanies.Where(company =>
                Matches(company.Name, name) &&
                Matches(company.City, city) &&
                Matches(company.Pkd, mainPkdCode)).Take(10).ToArray();

            return Results.Ok(new
            {
                page = 1,
                pageSize = rows.Length,
                totalRows = rows.Length,
                returnedRows = rows.Length,
                columns = selectedColumns,
                tokenCost = Math.Max(1, rows.Length * selectedColumns.Length),
                demoUsesRemaining = Math.Max(0, AnonymousDemoLimit - used),
                items = rows.Select(row => ProjectDemoRow(row, selectedColumns))
            });
        })
        .ExcludeFromDescription();

        return app;
    }
    private static string GetAnonymousDemoKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var agent = context.Request.Headers.UserAgent.ToString();
        return string.Concat(ip, "|", agent).ToUpperInvariant();
    }

    private static bool Matches(string value, string? query) =>
        string.IsNullOrWhiteSpace(query) || value.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string[] ResolveDemoColumns(string? columns)
    {
        var allowed = new[] { "nip", "name", "city", "email", "www", "pkd", "status" };
        if (string.IsNullOrWhiteSpace(columns))
        {
            return allowed;
        }

        var requested = columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(column => allowed.Contains(column, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return requested.Length == 0 ? allowed : requested;
    }

    private static Dictionary<string, object?> ProjectDemoRow(DemoCompany row, string[] columns)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            result[column] = column.ToLowerInvariant() switch
            {
                "nip" => row.Nip,
                "name" => row.Name,
                "city" => row.City,
                "email" => row.Email,
                "www" => row.Www,
                "pkd" => row.Pkd,
                "status" => row.Status,
                _ => null
            };
        }

        return result;
    }

    private static readonly DemoCompany[] DemoCompanies =
    [
        new("7312045678", "FIRMA ABC JAN KOWALSKI", "Warszawa", "biuro@firmaabc.pl", "firmaabc.pl", "62.01.Z", "Aktywny"),
        new("9491832736", "PV SOLUTIONS SPOLKA Z O.O.", "Krakow", "kontakt@pvsolutions.pl", "pvsolutions.pl", "43.21.Z", "Aktywny"),
        new("8762459076", "SUN ENERGY S.C.", "Gdansk", "biuro@sunenergy.pl", "sunenergy.pl", "43.21.Z", "Aktywny"),
        new("5223001189", "EKO INSTALACJE DARIUSZ NOWAK", "Poznan", "d.nowak@ekoinstalacje.pl", "ekoinstalacje.pl", "43.21.Z", "Zawieszony"),
        new("1132894410", "SOFTWARE LAB ANNA WISNIEWSKA", "Warszawa", "hello@softwarelab.pl", "softwarelab.pl", "62.02.Z", "Aktywny")
    ];

    private sealed record DemoCompany(string Nip, string Name, string City, string Email, string Www, string Pkd, string Status);
    private const string HomeHtml = """
<!doctype html>
<html lang="pl">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>leadbase.network - API danych firm z CEIDG</title>
  <meta name="description" content="leadbase.network udostêpnia dane firm z CEIDG przez API rozliczane tokenami. Wybieraj kolumny, filtruj rekordy i p³aæ za realnie u¿yte dane.">
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&display=swap" rel="stylesheet">
  <link rel="stylesheet" href="/leadbase.css">
</head>
<body>
  <header class="site-header">
    <a class="brand" href="/" aria-label="leadbase.network home">
      <span class="brand-mark">lb</span>
      <span>leadbase.network</span>
    </a>
    <nav class="nav" aria-label="G³ówna nawigacja">
      <a href="#produkt">Produkt</a>
      <a href="#api">API</a>
      <a href="#cennik">Cennik</a>
      <a href="/swagger">Dokumentacja</a>
      <a href="#dashboard">Panel</a>
    </nav>
    <div class="header-actions">
      <a class="button button-ghost" href="/swagger">Swagger</a>
      <a class="button button-primary" href="/swagger/index.html#/Authentication/post_auth_register">Utwórz konto</a>
    </div>
  </header>

  <main>
    <section class="hero" id="produkt">
      <div class="hero-copy">
        <h1>Dane firm z CEIDG przez API rozliczane tokenami</h1>
        <p>Wyszukuj firmy, wybieraj dok³adnie te kolumny, których potrzebujesz, korzystaj ze stronicowanego API i p³aæ tylko za realnie pobrane dane.</p>
        <div class="hero-actions">
          <a class="button button-primary button-large" href="/swagger/index.html#/Authentication/post_auth_register">Utwórz konto</a>
          <a class="button button-secondary button-large" href="/swagger">Zobacz Swagger</a>
        </div>
        <div class="proof-grid" aria-label="Najwa¿niejsze funkcje">
          <div><strong>Dane z CEIDG</strong><span>lokalny mirror PostgreSQL</span></div>
          <div><strong>Wybór kolumn</strong><span>ni¿szy koszt zapytañ</span></div>
          <div><strong>Paginacja</strong><span>kontrola du¿ych wyników</span></div>
          <div><strong>Tokeny</strong><span>rozliczenie za u¿ycie</span></div>
        </div>
      </div>

      <div class="hero-product" id="api" aria-label="Podgl¹d API leadbase.network">
        <div class="code-window">
          <div class="window-bar"><span>GET</span><code>/companies?columns=nip,name,email,www,pkd</code><b>200 OK</b></div>
          <pre>{
  "success": true,
  "items": [
    {
      "nip": "7312045678",
      "name": "FIRMA ABC JAN KOWALSKI",
      "email": "biuro@firmaabc.pl",
      "www": "https://firmaabc.pl",
      "pkd": "62.01.Z",
      "status": "Aktywny"
    }
  ],
  "pagination": { "page": 1, "pageSize": 50 },
  "tokensConsumed": 2
}</pre>
        </div>
        <aside class="token-card">
          <span>Twoje tokeny</span>
          <strong>12 450</strong>
          <div class="meter"><i></i></div>
          <dl><dt>Zu¿yte w miesi¹cu</dt><dd>7 550</dd><dt>Pozosta³e</dt><dd>12 450</dd></dl>
        </aside>
      </div>
    </section>

    <section class="endpoint-lab" id="tester" aria-label="Graficzny tester endpointu firm">
      <div class="section-head compact">
        <div><h2>Testuj endpoint firm</h2><p>Wykonaj 2 darmowe zapytania demo. Potem wymagamy rejestracji i klucza API.</p></div>
        <a class="button button-ghost" href="/swagger/index.html#/Companies/get_companies">Swagger</a>
      </div>
      <div class="lab-grid">
        <form class="endpoint-form" id="endpoint-form">
          <label>Nazwa firmy<input name="name" placeholder="np. energia, instalacje, software"></label>
          <label>Miasto<input name="city" placeholder="np. Warszawa"></label>
          <label>PKD<input name="mainPkdCode" placeholder="np. 43.21.Z"></label>
          <label>API key dla konta<input name="apiKey" placeholder="Opcjonalnie: ceidg_..."></label>
          <fieldset>
            <legend>Zwracane kolumny</legend>
            <label><input type="checkbox" name="columns" value="nip" checked> NIP</label>
            <label><input type="checkbox" name="columns" value="name" checked> Nazwa</label>
            <label><input type="checkbox" name="columns" value="city" checked> Miasto</label>
            <label><input type="checkbox" name="columns" value="email" checked> Email</label>
            <label><input type="checkbox" name="columns" value="www" checked> WWW</label>
            <label><input type="checkbox" name="columns" value="pkd" checked> PKD</label>
            <label><input type="checkbox" name="columns" value="status" checked> Status</label>
          </fieldset>
          <button class="button button-primary button-large" type="submit">Testuj endpoint</button>
          <p class="lab-note" id="demo-counter">Demo: 2 darmowe zapytania bez konta.</p>
        </form>
        <div class="endpoint-result">
          <div class="result-toolbar"><span id="result-status">Gotowy do testu</span><code id="result-url">GET /demo/companies</code></div>
          <div class="table-wrap"><table id="endpoint-table"><thead></thead><tbody></tbody></table></div>
          <pre id="result-json">Wynik zapytania pojawi siê tutaj.</pre>
        </div>
      </div>
      <div class="register-gate" id="register-gate" hidden>
        <div><h3>Limit demo zosta³ wykorzystany</h3><p>Utwórz konto, odbierz startowe tokeny i testuj endpointy bez ograniczenia demo.</p></div>
        <a class="button button-primary" href="/swagger/index.html#/Authentication/post_auth_register">Zarejestruj siê</a>
      </div>
    </section>

    <section class="steps">
      <div class="section-head"><h2>Jak dzia³a leadbase.network?</h2><p>Od rejestracji do pierwszego zapytania bez rêcznego obrabiania plików CEIDG.</p></div>
      <div class="step-grid">
        <article><span>1</span><h3>Utwórz konto</h3><p>Dostajesz pulê darmowych tokenów na start.</p></article>
        <article><span>2</span><h3>Pobierz klucz API</h3><p>Klucz wysy³asz w nag³ówku <code>X-Api-Key</code>.</p></article>
        <article><span>3</span><h3>Wybierz kolumny</h3><p>Im mniej danych zwracasz, tym ni¿szy koszt.</p></article>
        <article><span>4</span><h3>Monitoruj zu¿ycie</h3><p>Saldo i historia zapytañ s¹ zapisywane w ledgerze.</p></article>
      </div>
    </section>

    <section class="pricing" id="cennik">
      <div class="section-head"><h2>Pakiety tokenów</h2><p>Model docelowy: u¿ytkownik kupuje pulê tokenów i zu¿ywa j¹ na zapytania API.</p></div>
      <div class="price-grid">
        <article><small>START</small><h3>49 z³</h3><p>500 tokenów</p><ul><li>Wa¿noœæ 30 dni</li><li>Wsparcie email</li></ul><a class="button button-ghost" href="/swagger">Wybierz pakiet</a></article>
        <article class="featured"><small>PRO</small><h3>199 z³</h3><p>2 500 tokenów</p><ul><li>Wa¿noœæ 30 dni</li><li>Wsparcie priorytetowe</li></ul><a class="button button-primary" href="/swagger">Wybierz pakiet</a></article>
        <article><small>BUSINESS</small><h3>499 z³</h3><p>7 500 tokenów</p><ul><li>Wa¿noœæ 30 dni</li><li>Indywidualne limity</li></ul><a class="button button-ghost" href="/swagger">Wybierz pakiet</a></article>
        <article><small>ENTERPRISE</small><h3>Indywidualnie</h3><p>Wysoki wolumen</p><ul><li>SLA</li><li>Dedykowane warunki</li></ul><a class="button button-ghost" href="mailto:kontakt@leadbase.network">Skontaktuj siê</a></article>
      </div>
    </section>

    <section class="docs-band">
      <div><h2>Dokumentacja dla developerów</h2><p>Swagger UI, przyk³ady zapytañ i lista dostêpnych kolumn s¹ dostêpne od razu w aplikacji.</p></div>
      <a class="button button-primary" href="/swagger">PrzejdŸ do dokumentacji</a>
    </section>

    <section class="dashboard" id="dashboard">
      <div class="section-head"><h2>Panel u¿ytkownika</h2><p>Kolejny etap produktu: zakup tokenów, historia u¿ycia, klucze API, faktury i limity.</p></div>
      <div class="dashboard-frame">
        <aside><strong>leadbase</strong><a>Podsumowanie</a><a>Tokeny i p³atnoœci</a><a>Historia u¿ycia</a><a>Klucze API</a><a>Faktury</a></aside>
        <div class="dash-main">
          <div class="metrics"><article><span>Dostêpne tokeny</span><b>12 450</b></article><article><span>Zu¿ycie miesi¹c</span><b>7 550</b></article><article><span>Zapytania</span><b>3 842</b></article><article><span>B³êdy</span><b>0.25%</b></article></div>
          <div class="chart"><i style="height:35%"></i><i style="height:70%"></i><i style="height:48%"></i><i style="height:85%"></i><i style="height:42%"></i><i style="height:62%"></i><i style="height:95%"></i><i style="height:55%"></i><i style="height:78%"></i><i style="height:44%"></i><i style="height:68%"></i><i style="height:88%"></i></div>
        </div>
      </div>
    </section>
  </main>

  <footer class="footer"><span>leadbase.network</span><span>Dane CEIDG przez API. Token billing. PostgreSQL mirror.</span><a href="/swagger">Swagger</a></footer>
  <script src="/leadbase.js"></script>
</body>
</html>
""";
}

