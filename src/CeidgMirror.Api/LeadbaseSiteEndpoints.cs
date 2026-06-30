using System.Collections.Concurrent;
using System.Security.Claims;

namespace CeidgMirror.Api;

public static class LeadbaseSiteEndpoints
{
    private const int AnonymousDemoLimit = 2;
    private static readonly ConcurrentDictionary<string, int> AnonymousDemoUsage = new(StringComparer.Ordinal);

    public static WebApplication MapLeadbaseSite(this WebApplication app)
    {
        app.MapGet("/", (HttpContext context) => Results.Content(RenderHomeHtml(context), "text/html; charset=utf-8"))
            .ExcludeFromDescription();

        app.MapGet("/docs", () => Results.Redirect("/swagger"))
            .ExcludeFromDescription();

        app.MapGet("/app", async (HttpContext context, ProductApiStore store, CancellationToken cancellationToken) =>
        {
            var userId = GetSignedInUserId(context);
            if (userId is null)
            {
                return Results.Redirect("/login");
            }

            var account = await store.GetAccountPanelAsync(userId.Value, cancellationToken);
            return account is null
                ? Results.Redirect("/login")
                : Results.Content(RenderAppHtml(account), "text/html; charset=utf-8");
        })
        .ExcludeFromDescription();

        app.MapPost("/app/api-keys", async (HttpContext context, ProductApiStore store, CancellationToken cancellationToken) =>
        {
            var userId = GetSignedInUserId(context);
            if (userId is null)
            {
                return Results.Redirect("/login");
            }

            var form = await context.Request.ReadFormAsync(cancellationToken);
            var keyName = form["keyName"].ToString().Trim();
            var created = await store.CreateApiKeyAsync(userId.Value, string.IsNullOrWhiteSpace(keyName) ? "Panel" : keyName, cancellationToken);
            return Results.Content(RenderCreatedApiKeyHtml(created.ApiKey, created.KeyPrefix), "text/html; charset=utf-8");
        })
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
                Matches(company.MainPkdCode, mainPkdCode)).Take(10).ToArray();

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

    private static string RenderHomeHtml(HttpContext context)
    {
        var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
        var email = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.Identity?.Name ?? "konto";
        var headerActions = isAuthenticated
            ? $"<a class=\"button button-ghost\" href=\"/app\">Panel</a><a class=\"button button-primary\" href=\"/logout\">Wyloguj się</a>"
            : "<a class=\"button button-ghost\" href=\"/login\">Logowanie</a><a class=\"button button-primary\" href=\"/register\">Utwórz konto</a>";
        var heroActions = isAuthenticated
            ? $"<a class=\"button button-primary button-large\" href=\"/app\">Przejdź do panelu</a><a class=\"button button-secondary button-large\" href=\"/logout\">Wyloguj się</a>"
            : "<a class=\"button button-primary button-large\" href=\"/register\">Utwórz konto</a><a class=\"button button-secondary button-large\" href=\"/swagger\">Zobacz Swagger</a>";
        var accountLabel = isAuthenticated ? $"<span class=\"account-chip\">{Html(email)}</span>" : string.Empty;

        return HomeHtml
            .Replace("{{ACCOUNT_ACTIONS}}", headerActions)
            .Replace("{{HERO_ACTIONS}}", heroActions)
            .Replace("{{ACCOUNT_LABEL}}", accountLabel);
    }

    private static Guid? GetSignedInUserId(HttpContext context)
    {
        var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static string RenderAppHtml(AccountPanel account) => $"""
<!doctype html>
<html lang="pl">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Panel - leadbase.network</title>
  <link rel="stylesheet" href="/leadbase.css">
</head>
<body>
  <header class="site-header">
    <a class="brand" href="/"><span class="brand-mark">lb</span><span>leadbase.network</span></a>
    <nav class="nav"><a href="/app">Panel</a><a href="/swagger">Dokumentacja</a><a href="/#tester">Tester</a></nav>
    <div class="header-actions"><span class="account-chip">{Html(account.Email)}</span><a class="button button-primary" href="/logout">Wyloguj się</a></div>
  </header>
  <main class="real-app-shell">
    <section class="real-app-head">
      <div><h1>Panel użytkownika</h1><p>Zarządzaj kluczami API, tokenami i historią użycia leadbase.network.</p></div>
      <a class="button button-secondary" href="/swagger">Dokumentacja API</a>
    </section>
    <section class="metrics app-metrics">
      <article><span>Email</span><b>{Html(account.Email)}</b></article>
      <article><span>Dostępne tokeny</span><b>{account.TokenBalance:N0}</b></article>
      <article><span>Aktywne klucze API</span><b>{account.ApiKeyCount}</b></article>
      <article><span>Zapytania API</span><b>{account.QueryCount}</b></article>
    </section>
    <section class="dashboard-frame real-app-frame">
      <aside><strong>leadbase</strong><a href="#summary">Podsumowanie</a><a href="#api-keys">Klucze API</a><a href="#tokens">Tokeny</a><a href="#usage">Historia użycia</a><a href="/swagger">Swagger</a></aside>
      <div class="dash-main account-dashboard">
        <section class="panel-section" id="api-keys">
          <div class="panel-section-head"><div><h2>Klucze API</h2><p>Pełny klucz pokazujemy tylko raz po utworzeniu. W bazie przechowujemy hash oraz prefiks.</p></div></div>
          <form class="key-create-form" method="post" action="/app/api-keys">
            <label>Nazwa klucza <input name="keyName" maxlength="80" placeholder="np. Integracja CRM" autocomplete="off"></label>
            <button class="button button-primary" type="submit">Utwórz klucz</button>
          </form>
          {RenderApiKeys(account.ApiKeys)}
        </section>
        <section class="panel-section" id="tokens">
          <div class="panel-section-head"><div><h2>Tokeny</h2><p>Ostatnie obciążenia i doładowania salda.</p></div></div>
          {RenderLedger(account.Ledger)}
        </section>
        <section class="panel-section" id="usage">
          <div class="panel-section-head"><div><h2>Historia użycia API</h2><p>Ostatnie zapytania z kosztem tokenowym i wybranymi kolumnami.</p></div></div>
          {RenderQueryLogs(account.QueryLogs)}
        </section>
      </div>
    </section>
  </main>
</body>
</html>
""";

    private static string RenderCreatedApiKeyHtml(string apiKey, string keyPrefix) => $"""
<!doctype html>
<html lang="pl">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Nowy klucz API - leadbase.network</title>
  <link rel="stylesheet" href="/leadbase.css">
</head>
<body class="account-page">
  <main class="account-shell">
    <section class="account-form api-key-created">
      <h1>Klucz API został utworzony</h1>
      <p>Zapisz go teraz w bezpiecznym miejscu. Po opuszczeniu tej strony widoczny będzie tylko prefiks.</p>
      <div class="one-time-key"><span>Prefiks</span><strong>{Html(keyPrefix)}</strong></div>
      <code>{Html(apiKey)}</code>
      <a class="button button-primary" href="/app">Wróć do panelu</a>
    </section>
  </main>
</body>
</html>
""";

    private static string RenderApiKeys(IReadOnlyList<AccountApiKey> apiKeys)
    {
        if (apiKeys.Count == 0)
        {
            return "<div class=\"empty-state\">Nie masz jeszcze aktywnych kluczy API.</div>";
        }

        return """
          <div class="table-wrap panel-table-wrap"><table class="panel-table"><thead><tr><th>Nazwa</th><th>Prefiks</th><th>Utworzony</th><th>Ostatnie użycie</th></tr></thead><tbody>
""" + string.Join(string.Empty, apiKeys.Select(key => $"""
            <tr><td>{Html(key.Name ?? "Bez nazwy")}</td><td><code>{Html(key.KeyPrefix)}</code></td><td>{FormatDate(key.CreatedAtUtc)}</td><td>{FormatDate(key.LastUsedAtUtc)}</td></tr>
""")) + """
          </tbody></table></div>
""";
    }

    private static string RenderLedger(IReadOnlyList<AccountLedgerEntry> ledger)
    {
        if (ledger.Count == 0)
        {
            return "<div class=\"empty-state\">Brak operacji tokenowych dla tego konta.</div>";
        }

        return """
          <div class="table-wrap panel-table-wrap"><table class="panel-table"><thead><tr><th>Data</th><th>Powód</th><th>Zmiana</th><th>Saldo po</th></tr></thead><tbody>
""" + string.Join(string.Empty, ledger.Select(entry => $"""
            <tr><td>{FormatDate(entry.CreatedAtUtc)}</td><td>{Html(ReasonLabel(entry.Reason))}</td><td class="{(entry.Delta < 0 ? "negative" : "positive")}">{entry.Delta:+#,0;-#,0;0}</td><td>{entry.BalanceAfter:N0}</td></tr>
""")) + """
          </tbody></table></div>
""";
    }

    private static string RenderQueryLogs(IReadOnlyList<AccountQueryLog> logs)
    {
        if (logs.Count == 0)
        {
            return "<div class=\"empty-state\">Brak wykonanych zapytań API.</div>";
        }

        return """
          <div class="table-wrap panel-table-wrap"><table class="panel-table"><thead><tr><th>Data</th><th>Endpoint</th><th>Kolumny</th><th>Wiersze</th><th>Koszt</th></tr></thead><tbody>
""" + string.Join(string.Empty, logs.Select(log => $"""
            <tr><td>{FormatDate(log.CreatedAtUtc)}</td><td>{Html(log.Endpoint)}</td><td>{Html(string.Join(", ", log.SelectedColumns))}</td><td>{log.ReturnedRows:N0}</td><td>{log.TokenCost:N0}</td></tr>
""")) + """
          </tbody></table></div>
""";
    }

    private static string FormatDate(DateTimeOffset? value) => value is null ? "-" : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    private static string ReasonLabel(string reason) => reason switch
    {
        "registration_grant" => "Pakiet startowy",
        "company_search" => "Zapytanie do firm",
        _ => reason
    };

    private static string Html(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
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
        var allowed = new[] { "ceidgId", "nip", "regon", "name", "status", "ownerFirstName", "ownerLastName", "city", "voivodeship", "street", "postalCode", "mainPkdCode", "phone", "email", "website", "pkdCodes", "rawDetailPayload" };
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
                "ceidgid" => row.CeidgId,
                "nip" => row.Nip,
                "regon" => row.Regon,
                "name" => row.Name,
                "status" => row.Status,
                "ownerfirstname" => row.OwnerFirstName,
                "ownerlastname" => row.OwnerLastName,
                "city" => row.City,
                "voivodeship" => row.Voivodeship,
                "street" => row.Street,
                "postalcode" => row.PostalCode,
                "mainpkdcode" => row.MainPkdCode,
                "phone" => row.Phone,
                "email" => row.Email,
                "website" => row.Website,
                "pkdcodes" => row.PkdCodes,
                "rawdetailpayload" => row.RawDetailPayload,
                _ => null
            };
        }

        return result;
    }

    private static readonly DemoCompany[] DemoCompanies =
    [
        new("b18d8d43-4e20-49c0-8e21-000000000001", "7312045678", "141234567", "FIRMA ABC JAN KOWALSKI", "Aktywny", "Jan", "Kowalski", "Warszawa", "mazowieckie", "Marszalkowska 10", "00-590", "62.01.Z", "+48 501 100 200", "biuro@firmaabc.pl", "firmaabc.pl", "[\"62.01.Z\",\"62.02.Z\"]", "{\"source\":\"demo\",\"contact\":true}"),
        new("b18d8d43-4e20-49c0-8e21-000000000002", "9491832736", "122334455", "PV SOLUTIONS SPOLKA Z O.O.", "Aktywny", "Anna", "Nowak", "Krakow", "malopolskie", "Dluga 8", "31-146", "43.21.Z", "+48 512 333 444", "kontakt@pvsolutions.pl", "pvsolutions.pl", "[\"43.21.Z\",\"35.11.Z\"]", "{\"source\":\"demo\",\"contact\":true}"),
        new("b18d8d43-4e20-49c0-8e21-000000000003", "8762459076", "987654321", "SUN ENERGY S.C.", "Aktywny", "Piotr", "Zielinski", "Gdansk", "pomorskie", "Grunwaldzka 22", "80-236", "43.21.Z", "+48 533 220 110", "biuro@sunenergy.pl", "sunenergy.pl", "[\"43.21.Z\"]", "{\"source\":\"demo\",\"contact\":true}"),
        new("b18d8d43-4e20-49c0-8e21-000000000004", "5223001189", "556677889", "EKO INSTALACJE DARIUSZ NOWAK", "Zawieszony", "Dariusz", "Nowak", "Poznan", "wielkopolskie", "Polna 5", "60-535", "43.21.Z", "+48 600 700 800", "d.nowak@ekoinstalacje.pl", "ekoinstalacje.pl", "[\"43.21.Z\",\"71.12.Z\"]", "{\"source\":\"demo\",\"contact\":true}"),
        new("b18d8d43-4e20-49c0-8e21-000000000005", "1132894410", "101202303", "SOFTWARE LAB ANNA WISNIEWSKA", "Aktywny", "Anna", "Wisniewska", "Warszawa", "mazowieckie", "Prosta 51", "00-838", "62.02.Z", "+48 577 101 202", "hello@softwarelab.pl", "softwarelab.pl", "[\"62.02.Z\",\"63.11.Z\"]", "{\"source\":\"demo\",\"contact\":true}")
    ];

    private sealed record DemoCompany(string CeidgId, string Nip, string Regon, string Name, string Status, string OwnerFirstName, string OwnerLastName, string City, string Voivodeship, string Street, string PostalCode, string MainPkdCode, string Phone, string Email, string Website, string PkdCodes, string RawDetailPayload);
    private const string HomeHtml = """
<!doctype html>
<html lang="pl">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>leadbase.network - API danych firm z CEIDG</title>
  <meta name="description" content="leadbase.network udostępnia dane firm z CEIDG przez API rozliczane tokenami. Wybieraj kolumny, filtruj rekordy i płać za realnie użyte dane.">
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
    <nav class="nav" aria-label="Główna nawigacja">
      <a href="#produkt">Produkt</a>
      <a href="#api">API</a>
      <a href="#cennik">Cennik</a>
      <a href="/swagger">Dokumentacja</a>
      <a href="#dashboard">Panel</a>
    </nav>
    <div class="header-actions">
      <a class="button button-ghost" href="/swagger">Swagger</a>
      <a class="button button-primary" href="/register">Utwórz konto</a>
    </div>
  </header>

  <main>
    <section class="hero" id="produkt">
      <div class="hero-copy">
        <h1>Dane firm z CEIDG przez API rozliczane tokenami</h1>
        <p>Wyszukuj firmy, wybieraj dokładnie te kolumny, których potrzebujesz, korzystaj ze stronicowanego API i płać tylko za realnie pobrane dane.</p>
        <div class="hero-actions">
          <a class="button button-primary button-large" href="/register">Utwórz konto</a>
          <a class="button button-secondary button-large" href="/swagger">Zobacz Swagger</a>
        </div>
        <div class="proof-grid" aria-label="Najważniejsze funkcje">
          <div><strong>Dane z CEIDG</strong><span>lokalny mirror PostgreSQL</span></div>
          <div><strong>Wybór kolumn</strong><span>niższy koszt zapytań</span></div>
          <div><strong>Paginacja</strong><span>kontrola dużych wyników</span></div>
          <div><strong>Tokeny</strong><span>rozliczenie za użycie</span></div>
        </div>
      </div>

      <div class="hero-product" id="api" aria-label="Podgląd API leadbase.network">
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
          <dl><dt>Zużyte w miesiącu</dt><dd>7 550</dd><dt>Pozostałe</dt><dd>12 450</dd></dl>
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
            <label><input type="checkbox" name="columns" value="ceidgId"> CEIDG ID</label>
            <label><input type="checkbox" name="columns" value="nip" checked> NIP</label>
            <label><input type="checkbox" name="columns" value="regon"> REGON</label>
            <label><input type="checkbox" name="columns" value="name" checked> Nazwa</label>
            <label><input type="checkbox" name="columns" value="status" checked> Status</label>
            <label><input type="checkbox" name="columns" value="ownerFirstName"> Imię właściciela</label>
            <label><input type="checkbox" name="columns" value="ownerLastName"> Nazwisko właściciela</label>
            <label><input type="checkbox" name="columns" value="city" checked> Miasto</label>
            <label><input type="checkbox" name="columns" value="voivodeship"> Województwo</label>
            <label><input type="checkbox" name="columns" value="street"> Ulica</label>
            <label><input type="checkbox" name="columns" value="postalCode"> Kod pocztowy</label>
            <label><input type="checkbox" name="columns" value="mainPkdCode" checked> Główne PKD</label>
            <label><input type="checkbox" name="columns" value="phone" checked> Telefon</label>
            <label><input type="checkbox" name="columns" value="email" checked> Email</label>
            <label><input type="checkbox" name="columns" value="website" checked> WWW</label>
            <label><input type="checkbox" name="columns" value="pkdCodes"> Wszystkie PKD</label>
            <label><input type="checkbox" name="columns" value="rawDetailPayload"> Raw JSON</label>
          </fieldset>
          <button class="button button-primary button-large" type="submit">Testuj endpoint</button>
          <p class="lab-note" id="demo-counter">Demo: 2 darmowe zapytania bez konta.</p>
        </form>
        <div class="endpoint-result">
          <div class="result-toolbar"><span id="result-status">Gotowy do testu</span><code id="result-url">GET /demo/companies</code></div>
          <div class="table-wrap"><table id="endpoint-table"><thead></thead><tbody></tbody></table></div>
          <pre id="result-json">Wynik zapytania pojawi się tutaj.</pre>
        </div>
      </div>
      <div class="register-gate" id="register-gate" hidden>
        <div><h3>Limit demo został wykorzystany</h3><p>Utwórz konto, odbierz startowe tokeny i testuj endpointy bez ograniczenia demo.</p></div>
        <a class="button button-primary" href="/register">Zarejestruj się</a>
      </div>
    </section>

    <section class="steps">
      <div class="section-head"><h2>Jak działa leadbase.network?</h2><p>Od rejestracji do pierwszego zapytania bez ręcznego obrabiania plików CEIDG.</p></div>
      <div class="step-grid">
        <article><span>1</span><h3>Utwórz konto</h3><p>Dostajesz pulę darmowych tokenów na start.</p></article>
        <article><span>2</span><h3>Pobierz klucz API</h3><p>Klucz wysyłasz w nagłówku <code>X-Api-Key</code>.</p></article>
        <article><span>3</span><h3>Wybierz kolumny</h3><p>Im mniej danych zwracasz, tym niższy koszt.</p></article>
        <article><span>4</span><h3>Monitoruj zużycie</h3><p>Saldo i historia zapytań są zapisywane w ledgerze.</p></article>
      </div>
    </section>

    <section class="pricing" id="cennik">
      <div class="section-head"><h2>Pakiety tokenów</h2><p>Model docelowy: użytkownik kupuje pulę tokenów i zużywa ją na zapytania API.</p></div>
      <div class="price-grid">
        <article><small>START</small><h3>49 zł</h3><p>500 tokenów</p><ul><li>Ważność 30 dni</li><li>Wsparcie email</li></ul><a class="button button-ghost" href="/swagger">Wybierz pakiet</a></article>
        <article class="featured"><small>PRO</small><h3>199 zł</h3><p>2 500 tokenów</p><ul><li>Ważność 30 dni</li><li>Wsparcie priorytetowe</li></ul><a class="button button-primary" href="/swagger">Wybierz pakiet</a></article>
        <article><small>BUSINESS</small><h3>499 zł</h3><p>7 500 tokenów</p><ul><li>Ważność 30 dni</li><li>Indywidualne limity</li></ul><a class="button button-ghost" href="/swagger">Wybierz pakiet</a></article>
        <article><small>ENTERPRISE</small><h3>Indywidualnie</h3><p>Wysoki wolumen</p><ul><li>SLA</li><li>Dedykowane warunki</li></ul><a class="button button-ghost" href="mailto:kontakt@leadbase.network">Skontaktuj się</a></article>
      </div>
    </section>

    <section class="docs-band">
      <div><h2>Dokumentacja dla developerów</h2><p>Swagger UI, przykłady zapytań i lista dostępnych kolumn są dostępne od razu w aplikacji.</p></div>
      <a class="button button-primary" href="/swagger">Przejdź do dokumentacji</a>
    </section>

    <section class="dashboard" id="dashboard">
      <div class="section-head"><h2>Panel użytkownika</h2><p>Kolejny etap produktu: zakup tokenów, historia użycia, klucze API, faktury i limity.</p></div>
      <div class="dashboard-frame">
        <aside><strong>leadbase</strong><a>Podsumowanie</a><a>Tokeny i płatności</a><a>Historia użycia</a><a>Klucze API</a><a>Faktury</a></aside>
        <div class="dash-main">
          <div class="metrics"><article><span>Dostępne tokeny</span><b>12 450</b></article><article><span>Zużycie miesiąc</span><b>7 550</b></article><article><span>Zapytania</span><b>3 842</b></article><article><span>Błędy</span><b>0.25%</b></article></div>
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

