namespace CeidgMirror.Api;

internal static class LeadbaseDeveloperDocs
{
    public static string Render() => """
<!doctype html>
<html lang="pl">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Dokumentacja developerska - leadbase.network</title>
  <meta name="description" content="Dokumentacja techniczna leadbase.network API: autoryzacja, wyszukiwanie firm, kolumny, tokeny, analityka i przykłady integracji.">
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&display=swap" rel="stylesheet">
  <link rel="stylesheet" href="/leadbase.css">
</head>
<body class="docs-page">
  <header class="site-header docs-header">
    <a class="brand" href="/" aria-label="leadbase.network home">
      <span class="brand-mark">lb</span>
      <span>leadbase.network</span>
    </a>
    <nav class="nav" aria-label="Nawigacja dokumentacji">
      <a href="/docs">Dokumentacja</a>
      <a href="/docs#quickstart">Quickstart</a>
      <a href="/docs#examples">Przykłady</a>
      <a href="/docs#companies">Firmy</a>
      <a href="/docs#analytics">Analityka</a>
      <a href="/swagger">Swagger</a>
    </nav>
    <div class="header-actions">
      <a class="button button-ghost" href="/swagger">OpenAPI</a>
      <a class="button button-primary" href="/app">Panel</a>
    </div>
  </header>

  <main class="docs-shell">
    <aside class="docs-sidebar" aria-label="Spis treści">
      <strong>Dokumentacja</strong>
      <a href="#start">Start</a>
      <a href="#auth">Autoryzacja</a>
      <a href="#quickstart">Quickstart</a>
      <a href="#examples">Przykłady implementacji</a>
      <a href="#companies">Wyszukiwanie firm</a>
      <a href="#columns">Kolumny i koszt</a>
      <a href="#history">Aktualne i historyczne wpisy</a>
      <a href="#analytics">Analityka</a>
      <a href="#billing">Tokeny i pakiety</a>
      <a href="#errors">Błędy</a>
      <a href="#operations">Endpointy operacyjne</a>
      <a href="/swagger">Swagger / OpenAPI</a>
    </aside>

    <article class="docs-content">
      <section class="docs-hero" id="start">
        <div>
          <span class="docs-eyebrow">leadbase.network API</span>
          <h1>Dokumentacja techniczna dla developerów</h1>
          <p>Integruj dane firm z CEIDG i KRS przez jedno stronicowane API. Wybierasz kolumny, filtrujesz rekordy, kontrolujesz koszt tokenowy i korzystasz z agregatów analitycznych.</p>
          <div class="docs-actions">
            <a class="button button-primary" href="#quickstart">Zacznij integrację</a>
            <a class="button button-ghost" href="/swagger">Otwórz Swagger</a>
          </div>
        </div>
        <div class="docs-status-card">
          <span>Base URL</span>
          <code>https://leadbase.network</code>
          <span>Auth header</span>
          <code>X-Api-Key: ceidg_...</code>
          <span>Format</span>
          <code>JSON / UTF-8</code>
        </div>
      </section>

      <section class="docs-section" id="auth">
        <h2>Autoryzacja</h2>
        <p>Endpointy danych wymagają klucza API przekazanego w nagłówku <code>X-Api-Key</code>. Pełny klucz widoczny jest tylko raz po utworzeniu, a później w panelu pokazujemy prefiks i status klucza.</p>
        <div class="docs-grid two">
          <div class="docs-card">
            <h3>Rejestracja użytkownika</h3>
            <p>Tworzy konto, przyznaje startowe tokeny i zwraca pierwszy klucz API.</p>
            <pre><code>POST /auth/register
Content-Type: application/json

{
  "email": "dev@example.com",
  "password": "bezpieczne-haslo-123",
  "displayName": "Integracja CRM"
}</code></pre>
          </div>
          <div class="docs-card">
            <h3>Nowy klucz po logowaniu</h3>
            <p>Logowanie API może utworzyć dodatkowy klucz, który potem rozpoznasz po prefiksie w panelu.</p>
            <pre><code>POST /auth/login
Content-Type: application/json

{
  "email": "dev@example.com",
  "password": "bezpieczne-haslo-123",
  "keyName": "Backend production"
}</code></pre>
          </div>
        </div>
      </section>

      <section class="docs-section" id="quickstart">
        <h2>Quickstart</h2>
        <p>Najczęstszy scenariusz to sprawdzenie firmy po NIP, pobranie statusu i podstawowych danych identyfikacyjnych.</p>
        <pre><code>curl -X GET "https://leadbase.network/companies?nip=7312045678&amp;columns=nip,name,status,city,mainPkdCode" \
  -H "X-Api-Key: ceidg_TWÓJ_KLUCZ"</code></pre>
        <pre><code>{
  "page": 1,
  "pageSize": 25,
  "totalRows": 1,
  "returnedRows": 1,
  "columns": ["nip", "name", "status", "city", "mainPkdCode"],
  "tokenCost": 2,
  "tokenBalanceAfter": 4998,
  "items": [
    {
      "nip": "7312045678",
      "name": "Firma ABC Jan Kowalski",
      "status": "Aktywny",
      "city": "Warszawa",
      "mainPkdCode": "62.01.Z"
    }
  ]
}</code></pre>
      </section>

      <section class="docs-section" id="examples">
        <h2>Przykłady implementacji</h2>
        <p>W przykładach używamy tego samego zapytania: wyszukanie firmy po NIP i pobranie tylko wybranych kolumn. Klucz API trzymaj w zmiennej środowiskowej, sekrecie CI/CD albo managerze sekretów, a nie w kodzie źródłowym.</p>
        <div class="docs-grid two code-sample-grid">
          <div class="docs-card code-sample">
            <h3>C# / .NET</h3>
            <pre><code>using System.Net.Http.Headers;

var apiKey = Environment.GetEnvironmentVariable("LEADBASE_API_KEY");
using var http = new HttpClient
{
    BaseAddress = new Uri("https://leadbase.network")
};

http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

var url = "/companies?nip=7312045678&amp;columns=nip,name,status,city,mainPkdCode";
var json = await http.GetStringAsync(url);
Console.WriteLine(json);</code></pre>
          </div>

          <div class="docs-card code-sample">
            <h3>TypeScript</h3>
            <pre><code>type CompanyResponse = {
  returnedRows: number;
  tokenCost: number;
  items: Array&lt;Record&lt;string, unknown&gt;&gt;;
};

const apiKey = process.env.LEADBASE_API_KEY!;
const url = new URL("https://leadbase.network/companies");
url.searchParams.set("nip", "7312045678");
url.searchParams.set("columns", "nip,name,status,city,mainPkdCode");

const response = await fetch(url, {
  headers: { "X-Api-Key": apiKey }
});

if (!response.ok) throw new Error(await response.text());
const data = await response.json() as CompanyResponse;
console.log(data.items);</code></pre>
          </div>

          <div class="docs-card code-sample">
            <h3>JavaScript</h3>
            <pre><code>const apiKey = process.env.LEADBASE_API_KEY;
const params = new URLSearchParams({
  nip: "7312045678",
  columns: "nip,name,status,city,mainPkdCode"
});

const response = await fetch(`https://leadbase.network/companies?${params}`, {
  headers: { "X-Api-Key": apiKey }
});

if (!response.ok) {
  throw new Error(await response.text());
}

const data = await response.json();
console.log(data.items);</code></pre>
          </div>

          <div class="docs-card code-sample">
            <h3>Java</h3>
            <pre><code>import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;

var apiKey = System.getenv("LEADBASE_API_KEY");
var client = HttpClient.newHttpClient();
var uri = URI.create(
    "https://leadbase.network/companies?nip=7312045678"
    + "&amp;columns=nip,name,status,city,mainPkdCode");

var request = HttpRequest.newBuilder(uri)
    .header("X-Api-Key", apiKey)
    .GET()
    .build();

var response = client.send(request, HttpResponse.BodyHandlers.ofString());
if (response.statusCode() &gt;= 400) throw new RuntimeException(response.body());
System.out.println(response.body());</code></pre>
          </div>

          <div class="docs-card code-sample">
            <h3>PHP</h3>
            <pre><code>&lt;?php
$apiKey = getenv('LEADBASE_API_KEY');
$query = http_build_query([
    'nip' =&gt; '7312045678',
    'columns' =&gt; 'nip,name,status,city,mainPkdCode',
]);

$ch = curl_init("https://leadbase.network/companies?$query");
curl_setopt_array($ch, [
    CURLOPT_RETURNTRANSFER =&gt; true,
    CURLOPT_HTTPHEADER =&gt; ["X-Api-Key: $apiKey"],
]);

$body = curl_exec($ch);
$status = curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
curl_close($ch);

if ($status &gt;= 400) {
    throw new RuntimeException($body);
}

$data = json_decode($body, true);
print_r($data['items']);</code></pre>
          </div>

          <div class="docs-card code-sample">
            <h3>Rust</h3>
            <pre><code>#[tokio::main]
async fn main() -&gt; Result&lt;(), Box&lt;dyn std::error::Error&gt;&gt; {
    let api_key = std::env::var("LEADBASE_API_KEY")?;
    let client = reqwest::Client::new();

    let response = client
        .get("https://leadbase.network/companies")
        .query(&amp;[
            ("nip", "7312045678"),
            ("columns", "nip,name,status,city,mainPkdCode"),
        ])
        .header("X-Api-Key", api_key)
        .send()
        .await?
        .error_for_status()?;

    let body = response.text().await?;
    println!("{body}");
    Ok(())
}</code></pre>
          </div>

          <div class="docs-card code-sample">
            <h3>C++</h3>
            <pre><code>// Przykład z libcurl.
#include &lt;curl/curl.h&gt;
#include &lt;cstdlib&gt;
#include &lt;iostream&gt;
#include &lt;string&gt;

static size_t writeBody(char* ptr, size_t size, size_t nmemb, void* userdata) {
    auto* body = static_cast&lt;std::string*&gt;(userdata);
    body-&gt;append(ptr, size * nmemb);
    return size * nmemb;
}

int main() {
    const char* apiKey = std::getenv("LEADBASE_API_KEY");
    CURL* curl = curl_easy_init();
    std::string body;

    curl_easy_setopt(curl, CURLOPT_URL,
        "https://leadbase.network/companies?nip=7312045678&amp;columns=nip,name,status,city,mainPkdCode");
    curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, writeBody);
    curl_easy_setopt(curl, CURLOPT_WRITEDATA, &amp;body);

    std::string header = std::string("X-Api-Key: ") + apiKey;
    curl_slist* headers = nullptr;
    headers = curl_slist_append(headers, header.c_str());
    curl_easy_setopt(curl, CURLOPT_HTTPHEADER, headers);

    CURLcode result = curl_easy_perform(curl);
    if (result == CURLE_OK) std::cout &lt;&lt; body &lt;&lt; std::endl;

    curl_slist_free_all(headers);
    curl_easy_cleanup(curl);
}</code></pre>
          </div>

          <div class="docs-card code-sample">
            <h3>Python</h3>
            <pre><code>import os
import requests

api_key = os.environ["LEADBASE_API_KEY"]
response = requests.get(
    "https://leadbase.network/companies",
    headers={"X-Api-Key": api_key},
    params={
        "nip": "7312045678",
        "columns": "nip,name,status,city,mainPkdCode",
    },
    timeout=30,
)
response.raise_for_status()
print(response.json()["items"])</code></pre>
          </div>
        </div>
      </section>

      <section class="docs-section" id="companies">
        <h2>Wyszukiwanie firm</h2>
        <div class="endpoint-line"><span class="method get">GET</span><code>/companies</code></div>
        <p>Domyślnie endpoint zwraca aktualny rekord firmy. Wyniki są stronicowane, a liczba tokenów zależy od liczby zwróconych wierszy i zakresu kolumn.</p>
        <div class="docs-table-wrap">
          <table class="docs-table">
            <thead><tr><th>Parametr</th><th>Opis</th><th>Przykład</th></tr></thead>
            <tbody>
              <tr><td><code>columns</code></td><td>Lista kolumn po przecinku. Puste pole używa zestawu domyślnego.</td><td><code>nip,name,status,city</code></td></tr>
              <tr><td><code>page</code>, <code>pageSize</code></td><td>Stronicowanie. Maksymalny rozmiar strony jest ograniczony konfiguracją API.</td><td><code>page=1&amp;pageSize=50</code></td></tr>
              <tr><td><code>nip</code>, <code>regon</code>, <code>krsNumber</code></td><td>Filtry identyfikatorów firmy.</td><td><code>nip=7312045678</code></td></tr>
              <tr><td><code>name</code>, <code>city</code>, <code>voivodeship</code></td><td>Filtry tekstowe i lokalizacyjne.</td><td><code>city=Kraków</code></td></tr>
              <tr><td><code>status</code>, <code>mainPkdCode</code>, <code>legalForm</code></td><td>Segmentacja po statusie, PKD i formie prawnej.</td><td><code>mainPkdCode=62.01.Z</code></td></tr>
              <tr><td><code>registrySource</code>, <code>hasKrs</code></td><td>Źródło danych i obecność numeru KRS.</td><td><code>registrySource=KRS</code></td></tr>
              <tr><td><code>hasPhone</code>, <code>hasEmail</code>, <code>hasWebsite</code></td><td>Filtry dostępności danych kontaktowych bez publikowania ich na stronie głównej.</td><td><code>hasWebsite=true</code></td></tr>
              <tr><td><code>includeHistory</code></td><td>Po ustawieniu <code>true</code> zwraca również historyczne wpisy tej samej tożsamości firmy.</td><td><code>includeHistory=true</code></td></tr>
            </tbody>
          </table>
        </div>
      </section>

      <section class="docs-section" id="columns">
        <h2>Kolumny i koszt tokenowy</h2>
        <p>Pełną listę dostępnych pól pobierzesz z <code>GET /companies/columns</code>. Kolumny mają wagi, ale aktualny koszt zapytania liczony jest prostym modelem: <code>1 + liczba_wierszy * koszt_wiersza</code>.</p>
        <div class="docs-grid three">
          <div class="docs-card"><h3>Profil podstawowy</h3><p>Koszt wiersza: <strong>1 token</strong>. Identyfikatory, nazwa, status, forma prawna, lokalizacja, źródła i podstawowe daty.</p></div>
          <div class="docs-card"><h3>Dane kontaktowe lub PKD JSON</h3><p>Kontakt podnosi koszt wiersza o 1 token. <code>pkdCodes</code> również podnosi koszt o 1 token.</p></div>
          <div class="docs-card"><h3>Raw payload</h3><p>Surowe payloady <code>rawIndexPayload</code>, <code>rawDetailPayload</code>, <code>rawKrsPayload</code> dodają 10 tokenów do kosztu wiersza.</p></div>
        </div>
        <p>Najważniejsze kolumny: <code>nip</code>, <code>regon</code>, <code>name</code>, <code>status</code>, <code>legalForm</code>, <code>registeredOn</code>, <code>country</code>, <code>voivodeship</code>, <code>county</code>, <code>municipality</code>, <code>city</code>, <code>street</code>, <code>postalCode</code>, <code>mainPkdCode</code>, <code>pkdCodes</code>, <code>krsNumber</code>, <code>registrySources</code>, <code>phone</code>, <code>phoneMobile</code>, <code>phoneLandline</code>, <code>phonesJson</code>, <code>email</code>, <code>website</code>, <code>isCurrent</code>, <code>currentRank</code>.</p>
      </section>

      <section class="docs-section" id="history">
        <h2>Aktualne i historyczne wpisy</h2>
        <p>W tabeli przechowujemy historię zmian, ale API domyślnie pokazuje tylko aktualny rekord tożsamości firmy. To ogranicza duplikaty po NIP/REGON/KRS w standardowych integracjach.</p>
        <div class="docs-callout">
          <strong>Kiedy używać historii?</strong>
          <span>Użyj <code>includeHistory=true</code>, gdy chcesz analizować wcześniejsze statusy, zawieszenia, wykreślenia albo zmiany danych firmowych.</span>
        </div>
      </section>

      <section class="docs-section" id="analytics">
        <h2>Analityka</h2>
        <p>Endpointy analityczne zwracają agregaty i statystyki gotowe do raportów, dashboardów i oceny potencjału rynku.</p>
        <div class="docs-grid two">
          <div class="docs-card">
            <div class="endpoint-line"><span class="method get">GET</span><code>/analytics/summary</code></div>
            <p>Zwraca liczby firm, udziały statusów oraz udział rekordów z telefonem, mailem i stroną WWW dla wybranego filtra.</p>
          </div>
          <div class="docs-card">
            <div class="endpoint-line"><span class="method get">GET</span><code>/analytics/distribution</code></div>
            <p>Buduje ranking według wymiaru: <code>voivodeship</code>, <code>county</code>, <code>municipality</code>, <code>city</code>, <code>status</code>, <code>mainPkdCode</code>, <code>startedYear</code>, <code>registeredYear</code>, <code>sourceProfile</code>, <code>legalForm</code>.</p>
          </div>
        </div>
        <pre><code>curl "https://leadbase.network/analytics/distribution?dimension=city&amp;voivodeship=MAŁOPOLSKIE&amp;pkdPrefix=62&amp;status=Aktywny&amp;limit=10" \
  -H "X-Api-Key: ceidg_TWÓJ_KLUCZ"</code></pre>
        <p>Koszt zapytania analitycznego wynosi obecnie <strong>25 tokenów</strong>.</p>
      </section>

      <section class="docs-section" id="billing">
        <h2>Tokeny i pakiety</h2>
        <div class="docs-table-wrap">
          <table class="docs-table">
            <thead><tr><th>Pakiet</th><th>Tokeny</th><th>Cena netto</th><th>Średni koszt</th></tr></thead>
            <tbody>
              <tr><td>Starter</td><td>50 000</td><td>49 zł</td><td>0,98 zł / 1000 tokenów</td></tr>
              <tr><td>Growth</td><td>250 000</td><td>149 zł</td><td>0,596 zł / 1000 tokenów</td></tr>
              <tr><td>Scale</td><td>1 000 000</td><td>399 zł</td><td>0,399 zł / 1000 tokenów</td></tr>
              <tr><td>Enterprise</td><td>3 000 000</td><td>999 zł</td><td>0,333 zł / 1000 tokenów</td></tr>
            </tbody>
          </table>
        </div>
        <p>Saldo sprawdzisz przez <code>GET /account/balance</code>, a aktualny model cenowy przez <code>GET /billing/pricing</code>.</p>
      </section>

      <section class="docs-section" id="errors">
        <h2>Błędy i statusy HTTP</h2>
        <div class="docs-table-wrap">
          <table class="docs-table">
            <thead><tr><th>Status</th><th>Znaczenie</th><th>Co zrobić</th></tr></thead>
            <tbody>
              <tr><td><code>400</code></td><td>Nieprawidłowe parametry, np. brak obsługiwanego wymiaru analityki.</td><td>Sprawdź listę parametrów i wartości.</td></tr>
              <tr><td><code>401</code></td><td>Brak lub błędny klucz API.</td><td>Wyślij nagłówek <code>X-Api-Key</code> aktywnego klucza.</td></tr>
              <tr><td><code>402</code></td><td>Za mało tokenów na wykonanie zapytania.</td><td>Zmniejsz zakres danych albo doładuj saldo.</td></tr>
              <tr><td><code>409</code></td><td>Konflikt, np. próba rejestracji istniejącego emaila.</td><td>Zaloguj się albo użyj innego adresu.</td></tr>
            </tbody>
          </table>
        </div>
      </section>

      <section class="docs-section" id="operations">
        <h2>Endpointy operacyjne</h2>
        <p>Dla administracji i monitoringu dostępne są endpointy kontrolne:</p>
        <ul class="docs-list">
          <li><code>GET /operations/data-quality</code> - raport jakości danych, duplikatów, braków i nietypowych wartości.</li>
          <li><code>GET /operations/import-metrics</code> - metryki importu CEIDG/KRS, ostatni sukces, błędy, retry i throughput.</li>
          <li><code>GET /health</code> - prosty healthcheck aplikacji.</li>
        </ul>
        <div class="docs-footer-cta">
          <div>
            <h2>Potrzebujesz kontraktu OpenAPI?</h2>
            <p>Swagger UI zostaje dostępny jako narzędzie techniczne do testowania requestów i generowania klientów.</p>
          </div>
          <a class="button button-primary" href="/swagger">Otwórz Swagger</a>
        </div>
      </section>
    </article>
  </main>
</body>
</html>
""";
}
