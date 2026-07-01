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

        app.MapGet("/app", async (HttpContext context, ProductApiStore store, ProductOperationsStore operationsStore, CancellationToken cancellationToken) =>
        {
            var userId = GetSignedInUserId(context);
            if (userId is null)
            {
                return Results.Redirect("/login");
            }

            var account = await store.GetAccountPanelAsync(userId.Value, cancellationToken);
            var quality = await operationsStore.GetDataQualityReportAsync(cancellationToken);
            var imports = await operationsStore.GetImportMetricsAsync(cancellationToken);
            return account is null
                ? Results.Redirect("/login")
                : Results.Content(RenderAppHtml(account, quality, imports), "text/html; charset=utf-8");
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
            var expiresAtUtc = ParseApiKeyExpiration(form["expiresAtLocal"].ToString());
            var created = await store.CreateApiKeyAsync(userId.Value, string.IsNullOrWhiteSpace(keyName) ? "Panel" : keyName, expiresAtUtc, cancellationToken);
            return Results.Content(RenderCreatedApiKeyHtml(created.ApiKey, created.KeyPrefix), "text/html; charset=utf-8");
        })
        .ExcludeFromDescription();

        app.MapPost("/app/api-keys/{keyId:guid}/revoke", async (HttpContext context, Guid keyId, ProductApiStore store, CancellationToken cancellationToken) =>
        {
            var userId = GetSignedInUserId(context);
            if (userId is null)
            {
                return Results.Redirect("/login");
            }

            await store.RevokeApiKeyAsync(userId.Value, keyId, cancellationToken);
            return Results.Redirect("/app#api-keys");
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

    private static string RenderAppHtml(AccountPanel account, DataQualityReportResponse quality, ImportMetricsResponse imports) => $"""
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
      <aside><strong>leadbase</strong><a href="#summary">Podsumowanie</a><a href="#api-keys">Klucze API</a><a href="#tokens">Tokeny</a><a href="#usage">Historia użycia</a><a href="#data-quality">Jakość danych</a><a href="#imports">Importy</a><a href="/swagger">Swagger</a></aside>
      <div class="dash-main account-dashboard">
        <section class="panel-section" id="api-keys">
          <div class="panel-section-head"><div><h2>Klucze API</h2><p>Pełny klucz pokazujemy tylko raz po utworzeniu. W bazie przechowujemy hash oraz prefiks.</p></div></div>
          <form class="key-create-form" method="post" action="/app/api-keys">
            <label>Nazwa klucza <input name="keyName" maxlength="80" placeholder="np. Integracja CRM" autocomplete="off"></label>
            <label>Ważny do <input name="expiresAtLocal" type="datetime-local" autocomplete="off"><span>Opcjonalnie. Puste pole oznacza ważność bezterminową.</span></label>
            <button class="button button-primary" type="submit">Utwórz klucz</button>
          </form>
          {RenderApiKeys(account.ApiKeys)}
        </section>
        <section class="panel-section" id="tokens">
          <div class="panel-section-head"><div><h2>Tokeny</h2><p>Ostatnie obciążenia i doładowania salda.</p></div></div>
          {RenderLedger(account.Ledger)}
        </section>
        <section class="panel-section" id="usage">
          <div class="panel-section-head"><div><h2>Historia użycia API</h2><p>Ostatnie zapytania z kosztem tokenowym i wybranymi kolumnami. Starsze wpisy zachowują koszt naliczony według cennika obowiązującego w momencie zapytania.</p></div></div>
          {RenderQueryLogs(account.QueryLogs)}
        </section>
        <section class="panel-section" id="data-quality">
          <div class="panel-section-head"><div><h2>Jakość danych</h2><p>Raport kontrolny dla aktualnej tabeli firm po standaryzacji CEIDG/KRS.</p></div><a class="button button-ghost" href="/swagger/index.html#/Operations/get_operations_data_quality">Swagger</a></div>
          {RenderDataQuality(quality)}
        </section>
        <section class="panel-section" id="imports">
          <div class="panel-section-head"><div><h2>Importy CEIDG/KRS</h2><p>Ostatni postęp, throughput i błędy workerów pobierających dane źródłowe.</p></div><a class="button button-ghost" href="/swagger/index.html#/Operations/get_operations_import_metrics">Swagger</a></div>
          {RenderImportMetrics(imports)}
        </section>
      </div>
    </section>
  </main>
</body>
</html>
""";

    private static string RenderCreatedApiKeyHtml(string apiKey, string keyPrefix) => $$"""
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
      <p>To jedyny moment, w którym pokazujemy pełny klucz. Skopiuj go teraz i zapisz w bezpiecznym miejscu.</p>
      <div class="one-time-key"><span>Prefiks widoczny później w panelu</span><strong>{{Html(keyPrefix)}}</strong></div>
      <label class="api-key-copy-box">Pełny klucz API
        <input id="created-api-key" type="text" value="{{Html(apiKey)}}" readonly autocomplete="off" spellcheck="false">
      </label>
      <button class="button button-primary" type="button" id="copy-created-api-key">Kopiuj klucz</button>
      <a class="button button-secondary" href="/app">Wróć do panelu</a>
    </section>
  </main>
  <script>
    const keyInput = document.getElementById('created-api-key');
    const copyButton = document.getElementById('copy-created-api-key');
    copyButton.addEventListener('click', async () => {
      keyInput.focus();
      keyInput.select();
      try {
        await navigator.clipboard.writeText(keyInput.value);
      } catch {
        document.execCommand('copy');
      }
      copyButton.textContent = 'Skopiowano';
    });
  </script>
</body>
</html>
""";

    private static string RenderApiKeys(IReadOnlyList<AccountApiKey> apiKeys)
    {
        if (apiKeys.Count == 0)
        {
            return "<div class=\"empty-state\">Nie masz jeszcze kluczy API.</div>";
        }

        return "<div class=\"api-key-list\">" + string.Join(string.Empty, apiKeys.Select(RenderApiKeyCard)) + "</div>";
    }

    private static string RenderApiKeyCard(AccountApiKey key)
    {
        var isRevoked = key.RevokedAtUtc is not null;
        var isExpired = !isRevoked && key.ExpiresAtUtc is not null && key.ExpiresAtUtc <= DateTimeOffset.UtcNow;
        var cardClass = isRevoked || isExpired ? " api-key-card-inactive" : string.Empty;
        var status = isRevoked
            ? $"<span class=\"status-pill status-revoked\">Unieważniony</span><small>od {FormatDate(key.RevokedAtUtc)}</small>"
            : isExpired
                ? $"<span class=\"status-pill status-revoked\">Wygasł</span><small>od {FormatDate(key.ExpiresAtUtc)}</small>"
                : "<span class=\"status-pill status-active\">Aktywny</span>";
        var validUntil = key.ExpiresAtUtc is null ? "Bezterminowo" : FormatDate(key.ExpiresAtUtc);
        var actions = isRevoked
            ? "<span class=\"muted-cell\">Brak akcji</span>"
            : $"""
              <form class="inline-action-form" method="post" action="/app/api-keys/{key.Id}/revoke" onsubmit="return confirm('Unieważnić ten klucz API? Po tej operacji nie będzie można używać go w API.');">
                <button class="button button-danger button-small" type="submit">Unieważnij</button>
              </form>
""";

        return $"""
          <article class="api-key-card{cardClass}">
            <div class="api-key-card-main">
              <strong>{Html(key.Name ?? "Bez nazwy")}</strong>
              <code>{Html(key.KeyPrefix)}</code>
            </div>
            <dl class="api-key-meta">
              <div><dt>Status</dt><dd>{status}</dd></div>
              <div><dt>Ważny do</dt><dd>{Html(validUntil)}</dd></div>
              <div><dt>Utworzony</dt><dd>{FormatDate(key.CreatedAtUtc)}</dd></div>
              <div><dt>Ostatnie użycie</dt><dd>{FormatDate(key.LastUsedAtUtc)}</dd></div>
            </dl>
            <div class="api-key-actions">{actions}</div>
          </article>
""";
    }

    private static string RenderLedger(IReadOnlyList<AccountLedgerEntry> ledger)
    {
        if (ledger.Count == 0)
        {
            return "<div class=\"empty-state\">Brak operacji tokenowych dla tego konta.</div>";
        }

        return """
          <div class="table-wrap panel-table-wrap"><table class="panel-table"><thead><tr><th>Data</th><th>Klucz API</th><th>Powód</th><th>Zmiana</th><th>Saldo po</th></tr></thead><tbody>
""" + string.Join(string.Empty, ledger.Select(entry => $"""
            <tr><td>{FormatDate(entry.CreatedAtUtc)}</td><td>{RenderLedgerApiKey(entry)}</td><td>{Html(ReasonLabel(entry.Reason))}</td><td class="{(entry.Delta < 0 ? "negative" : "positive")}">{entry.Delta:+#,0;-#,0;0}</td><td>{entry.BalanceAfter:N0}</td></tr>
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



    private static string RenderDataQuality(DataQualityReportResponse quality)
    {
        var qualityCards = new[]
        {
            ("Firmy w bazie", quality.TotalCompanies.ToString("N0")),
            ("Puste NIP", quality.Identity.MissingNip.ToString("N0")),
            ("Puste REGON", quality.Identity.MissingRegon.ToString("N0")),
            ("Błędne kraje", quality.Address.InvalidCountryRows.ToString("N0")),
            ("Ulice z prefiksem", quality.Address.StreetWithPrefixRows.ToString("N0")),
            ("Błędne telefony", quality.Contact.InvalidPhoneRows.ToString("N0")),
            ("Duplikaty NIP", quality.Duplicates.DuplicateNipGroups.ToString("N0")),
            ("Duplikaty KRS", quality.Duplicates.DuplicateKrsGroups.ToString("N0"))
        };

        return $"""
          <div class="quality-grid">
            {string.Join(string.Empty, qualityCards.Select(card => $"<article><span>{Html(card.Item1)}</span><b>{Html(card.Item2)}</b></article>"))}
          </div>
          <div class="quality-details">
            <div><strong>Kontakt</strong><span>Brak telefonu: {quality.Contact.MissingPhoneRows:N0}</span><span>Brak email: {quality.Contact.MissingEmailRows:N0}</span><span>Brak WWW: {quality.Contact.MissingWebsiteRows:N0}</span></div>
            <div><strong>Identyfikatory</strong><span>Brak NIP i REGON: {quality.Identity.MissingNipAndRegon:N0}</span><span>Duplikowane REGON: {quality.Duplicates.DuplicateRegonGroups:N0} grup / {quality.Duplicates.DuplicateRegonRows:N0} wierszy</span></div>
            <div><strong>Wygenerowano</strong><span>{FormatDate(quality.GeneratedAtUtc)}</span></div>
          </div>
""";
    }

    private static string RenderImportMetrics(ImportMetricsResponse imports)
    {
        if (imports.Sources.Count == 0)
        {
            return """<div class="empty-state">Brak zapisanych importów CEIDG/KRS.</div>""";
        }

        return """
          <div class="import-card-list">
""" + string.Join(string.Empty, imports.Sources.Select(source => $"""
            <article class="import-card">
              <div><strong>{Html(source.ImportKind)}</strong><span class="{ImportStatusClass(source.LastRunStatus)}">{Html(source.LastRunStatus ?? "brak")}</span></div>
              <dl>
                <dt>Ostatni sukces</dt><dd>{FormatDate(source.LastCompletedRunFinishedAtUtc)}</dd>
                <dt>Ostatni checkpoint</dt><dd>{FormatDate(source.LastCheckpointAtUtc)}</dd>
                <dt>Zaimportowane</dt><dd>{source.ImportedFromCheckpoints:N0}</dd>
                <dt>Pominięte</dt><dd>{source.SkippedFromCheckpoints:N0}</dd>
                <dt>Błędy 24h</dt><dd>{source.FailedRuns24h:N0}</dd>
                <dt>Rekordy/min</dt><dd>{FormatDecimal(source.LastRunRecordsPerMinute)}</dd>
              </dl>
            </article>
""")) + $"""
          </div>
          <p class="panel-footnote">Raport wygenerowano: {FormatDate(imports.GeneratedAtUtc)}. Endpointy operacyjne nie pobierają tokenów.</p>
""";
    }

    private static string RenderLedgerApiKey(AccountLedgerEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ApiKeyPrefix))
        {
            return "<span class=\"muted-cell\">-</span>";
        }

        var name = string.IsNullOrWhiteSpace(entry.ApiKeyName) ? "Klucz API" : entry.ApiKeyName;
        return $"<span class=\"ledger-key\"><strong>{Html(name)}</strong><code>{Html(entry.ApiKeyPrefix)}</code></span>";
    }

    private static string FormatDate(DateTimeOffset? value) => value is null ? "-" : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    private static string FormatDecimal(decimal? value) => value is null ? "-" : value.Value.ToString("N2");
    private static string ImportStatusClass(string? status) => status?.Equals("completed", StringComparison.OrdinalIgnoreCase) == true ? "status-pill status-active" : "status-pill status-revoked";
    private static string ReasonLabel(string reason) => reason switch
    {
        "registration_grant" => "Pakiet startowy",
        "company_search" => "Zapytanie do firm",
        _ => reason
    };

    private static DateTimeOffset? ParseApiKeyExpiration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateTime.TryParse(value, out var localDateTime))
        {
            return null;
        }

        var unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified)).ToUniversalTime();
    }

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
        var allowed = new[] { "ceidgId", "nip", "regon", "name", "status", "legalForm", "registeredOn", "registrySources", "krsNumber", "ownerFirstName", "ownerLastName", "country", "city", "voivodeship", "county", "municipality", "street", "building", "unit", "postalCode", "mainPkdCode", "pkdCodes", "phone", "phoneMobile", "phoneLandline", "phonesJson", "email", "website" };
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
                "legalform" => row.LegalForm,
                "registeredon" => row.RegisteredOn,
                "registrysources" => row.RegistrySources,
                "krsnumber" => row.KrsNumber,
                "ownerfirstname" => row.OwnerFirstName,
                "ownerlastname" => row.OwnerLastName,
                "country" => row.Country,
                "city" => row.City,
                "voivodeship" => row.Voivodeship,
                "county" => row.County,
                "municipality" => row.Municipality,
                "street" => row.Street,
                "building" => row.Building,
                "unit" => row.Unit,
                "postalcode" => row.PostalCode,
                "mainpkdcode" => row.MainPkdCode,
                "pkdcodes" => row.PkdCodes,
                "phone" => row.Phone,
                "phonemobile" => row.PhoneMobile,
                "phonelandline" => row.PhoneLandline,
                "phonesjson" => row.PhonesJson,
                "email" => row.Email,
                "website" => row.Website,
                _ => null
            };
        }

        return result;
    }

    private static readonly DemoCompany[] DemoCompanies =
    [
        new("b18d8d43-4e20-49c0-8e21-000000000001", "7312045678", "141234567", "Firma ABC Jan Kowalski", "Aktywny", "Jednoosobowa działalność gospodarcza", "2018-03-14", "[\"CEIDG\"]", null, "Jan", "Kowalski", "PL", "Warszawa", "MAZOWIECKIE", "Warszawa", "Warszawa", "Marszałkowska", "10", null, "00-590", "62.01.Z", "[\"62.01.Z\",\"62.02.Z\"]", "+48600111222", "+48600111222", null, "[{\"type\":\"mobile\",\"value\":\"+48600111222\"}]", null, null),
        new("b18d8d43-4e20-49c0-8e21-000000000002", "9491832736", "122334455", "PV Solutions Sp. z o.o.", "Aktywny", "Spółka z ograniczoną odpowiedzialnością", "2020-09-02", "[\"KRS\"]", "0000123456", null, null, "PL", "Kraków", "MAŁOPOLSKIE", "Kraków", "Kraków", "Długa", "8", "4", "31-146", "43.21.Z", "[\"43.21.Z\",\"35.11.Z\"]", "+48 12 345 67 89", null, "+48 12 345 67 89", "[{\"type\":\"landline\",\"value\":\"+48 12 345 67 89\"}]", null, null),
        new("b18d8d43-4e20-49c0-8e21-000000000003", "8762459076", "987654321", "Sun Energy S.C.", "Aktywny", "Spółka cywilna", "2019-06-10", "[\"CEIDG\"]", null, "Piotr", "Zieliński", "PL", "Gdańsk", "POMORSKIE", "Gdańsk", "Gdańsk", "Grunwaldzka", "22", null, "80-236", "43.21.Z", "[\"43.21.Z\"]", null, null, null, "[]", null, null),
        new("b18d8d43-4e20-49c0-8e21-000000000004", "5223001189", "556677889", "Eko Instalacje Dariusz Nowak", "Zawieszony", "Jednoosobowa działalność gospodarcza", "2015-11-27", "[\"CEIDG\"]", null, "Dariusz", "Nowak", "PL", "Poznań", "WIELKOPOLSKIE", "Poznań", "Poznań", "Polna", "5", null, "60-535", "43.21.Z", "[\"43.21.Z\",\"71.12.Z\"]", "+48555111222, +48 61 223 45 67", "+48555111222", "+48 61 223 45 67", "[{\"type\":\"mobile\",\"value\":\"+48555111222\"},{\"type\":\"landline\",\"value\":\"+48 61 223 45 67\"}]", null, null),
        new("b18d8d43-4e20-49c0-8e21-000000000005", "1132894410", "101202303", "Software Lab Anna Wiśniewska", "Aktywny", "Jednoosobowa działalność gospodarcza", "2021-01-08", "[\"CEIDG\"]", null, "Anna", "Wiśniewska", "PL", "Warszawa", "MAZOWIECKIE", "Warszawa", "Warszawa", "Prosta", "51", null, "00-838", "62.02.Z", "[\"62.02.Z\",\"63.11.Z\"]", null, null, null, "[]", null, null)
    ];

    private sealed record DemoCompany(string CeidgId, string Nip, string Regon, string Name, string Status, string LegalForm, string RegisteredOn, string RegistrySources, string? KrsNumber, string? OwnerFirstName, string? OwnerLastName, string Country, string City, string Voivodeship, string County, string Municipality, string Street, string Building, string? Unit, string PostalCode, string MainPkdCode, string PkdCodes, string? Phone, string? PhoneMobile, string? PhoneLandline, string PhonesJson, string? Email, string? Website);
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
      <a href="#zastosowania">Zastosowania</a>
      <a href="#analityka">Analityka</a>
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
          <div class="window-bar"><span>GET</span><code>/companies?columns=nip,name,status,city,mainPkdCode</code><b>200 OK</b></div>
          <pre>{
  "success": true,
  "items": [
    {
      "nip": "7312045678",
      "name": "FIRMA ABC JAN KOWALSKI",
      "city": "Warszawa",
      "mainPkdCode": "62.01.Z",
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
            <label><input type="checkbox" name="columns" value="legalForm"> Forma prawna</label>
            <label><input type="checkbox" name="columns" value="registeredOn"> Data rejestracji</label>
            <label><input type="checkbox" name="columns" value="registrySources"> Źródła</label>
            <label><input type="checkbox" name="columns" value="krsNumber"> KRS</label>
            <label><input type="checkbox" name="columns" value="ownerFirstName"> Imię właściciela</label>
            <label><input type="checkbox" name="columns" value="ownerLastName"> Nazwisko właściciela</label>
            <label><input type="checkbox" name="columns" value="country"> Kraj</label>
            <label><input type="checkbox" name="columns" value="city" checked> Miasto</label>
            <label><input type="checkbox" name="columns" value="voivodeship"> Województwo</label>
            <label><input type="checkbox" name="columns" value="county"> Powiat</label>
            <label><input type="checkbox" name="columns" value="municipality"> Gmina</label>
            <label><input type="checkbox" name="columns" value="street"> Ulica</label>
            <label><input type="checkbox" name="columns" value="building"> Budynek</label>
            <label><input type="checkbox" name="columns" value="unit"> Lokal</label>
            <label><input type="checkbox" name="columns" value="postalCode"> Kod pocztowy</label>
            <label><input type="checkbox" name="columns" value="mainPkdCode" checked> Główne PKD</label>
            <label><input type="checkbox" name="columns" value="pkdCodes"> Wszystkie PKD</label>
            <label><input type="checkbox" name="columns" value="phoneMobile"> Telefon komórkowy</label>
            <label><input type="checkbox" name="columns" value="phoneLandline"> Telefon stacjonarny</label>
            <label><input type="checkbox" name="columns" value="phonesJson"> Telefony JSON</label>
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


    <section class="steps" id="zastosowania">
      <div class="section-head"><h2>Do czego używać API?</h2><p>leadbase.network nadaje się zarówno do szybkiej weryfikacji pojedynczej firmy, jak i do budowania procesów sprzedażowych, CRM oraz scoringu B2B.</p></div>
      <div class="step-grid">
        <article><span>1</span><h3>Weryfikacja po NIP</h3><p>Sprawdź, czy firma istnieje, jest aktywna, zawieszona lub wykreślona przed wystawieniem faktury, podpisaniem umowy albo wysyłką oferty.</p></article>
        <article><span>2</span><h3>Uzupełnianie CRM</h3><p>Dodawaj nazwę, status, lokalizację i PKD do rekordów leadów bez ręcznego przeszukiwania CEIDG.</p></article>
        <article><span>3</span><h3>Segmentacja leadów</h3><p>Buduj listy firm po województwie, mieście, statusie, głównym PKD, dacie rozpoczęcia działalności i profilu branżowym.</p></article>
        <article><span>4</span><h3>Monitoring kontrahentów</h3><p>Automatycznie odświeżaj status działalności i wykrywaj firmy zawieszone, zakończone lub z istotnymi zmianami danych.</p></article>
      </div>
    </section>

    <section class="steps" id="analityka">
      <div class="section-head"><h2>Analityka rynku</h2><p>Endpointy <code>/analytics</code> pokazują skalę branż, lokalizacji i statusów firm w formie agregatów gotowych do dashboardów i raportów.</p></div>
      <div class="step-grid">
        <article><span>PKD</span><h3>Potencjał branży</h3><p>Ile aktywnych firm o PKD 43.21.Z działa w Małopolsce i jaki procent wszystkich firm z tej branży stanowią.</p></article>
        <article><span>GEO</span><h3>Mapa rynku</h3><p>Ranking województw, powiatów, gmin lub miast według liczby aktywnych firm z wybranego segmentu.</p></article>
        <article><span>GEO</span><h3>Nasycenie lokalizacji</h3><p>Porównuj udział wybranej branży w województwach, powiatach, gminach i miastach.</p></article>
        <article><span>TREND</span><h3>Rocznik działalności</h3><p>Rozkład firm według roku rozpoczęcia działalności, statusu i lokalizacji, np. nowe firmy IT w dużych miastach.</p></article>
      </div>
      <div class="analytics-preview" aria-label="Przykładowy wykres analityczny">
        <div>
          <span>PKD 43.21.Z / Małopolskie</span>
          <strong>12 840 aktywnych firm</strong>
          <p>18,6% firm tej branży w analizowanym regionie</p>
        </div>
        <div class="mini-bars">
          <i style="height:42%"><b>Kraków</b></i>
          <i style="height:72%"><b>Tarnów</b></i>
          <i style="height:55%"><b>Nowy Sącz</b></i>
          <i style="height:88%"><b>Powiat krakowski</b></i>
          <i style="height:64%"><b>Oświęcim</b></i>
        </div>
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
      <div class="section-head"><h2>Pakiety tokenów</h2><p>Token rozliczamy jako koszt pobranego profilu firmy i zakresu danych. Podstawowe wyszukiwanie pozostaje tanie, a pełne surowe payloady mają wyższą wagę.</p></div>
      <div class="price-grid">
        <article><small>STARTER</small><h3>49 zł</h3><p>50 000 tokenów</p><strong class="unit-price">0,98 zł / 1000 tokenów</strong><ul><li>około 50 000 profili podstawowych</li><li>około 25 000 rozszerzonych profili</li><li>około 1 020 tokenów za 1 zł</li></ul><a class="button button-ghost" href="/register">Wybierz pakiet</a></article>
        <article class="featured"><small>GROWTH</small><h3>149 zł</h3><p>250 000 tokenów</p><strong class="unit-price">0,596 zł / 1000 tokenów</strong><ul><li>około 250 000 profili podstawowych</li><li>około 125 000 rozszerzonych profili</li><li>około 1 678 tokenów za 1 zł</li></ul><a class="button button-primary" href="/register">Wybierz pakiet</a></article>
        <article><small>SCALE</small><h3>399 zł</h3><p>1 000 000 tokenów</p><strong class="unit-price">0,399 zł / 1000 tokenów</strong><ul><li>około 1 000 000 profili podstawowych</li><li>około 500 000 rozszerzonych profili</li><li>około 2 506 tokenów za 1 zł</li></ul><a class="button button-ghost" href="/register">Wybierz pakiet</a></article>
        <article><small>ENTERPRISE</small><h3>999 zł</h3><p>3 000 000 tokenów</p><strong class="unit-price">0,333 zł / 1000 tokenów</strong><ul><li>wysoki wolumen</li><li>indywidualne limity i SLA</li><li>około 3 003 tokenów za 1 zł</li></ul><a class="button button-ghost" href="/register">Zapytaj o pakiet</a></article>
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

  <footer class="footer"><span>leadbase.network</span><span>Dane CEIDG przez API. Token billing. PostgreSQL mirror.</span><a href="/terms">Regulamin</a><a href="/privacy">Prywatność</a><a href="/cookies">Cookies</a><a href="/opt-out">Opt-out</a><a href="/swagger">Swagger</a></footer>
  <script src="/leadbase.js"></script>
</body>
</html>
""";
}

