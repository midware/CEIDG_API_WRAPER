namespace CeidgMirror.Api;

public static class LeadbaseSiteEndpoints
{
    public static WebApplication MapLeadbaseSite(this WebApplication app)
    {
        app.MapGet("/", () => Results.Content(HomeHtml, "text/html; charset=utf-8"))
            .ExcludeFromDescription();

        app.MapGet("/docs", () => Results.Redirect("/swagger"))
            .ExcludeFromDescription();

        app.MapGet("/app", () => Results.Content(HomeHtml.Replace("<body>", "<body data-scroll-target=\"dashboard\">"), "text/html; charset=utf-8"))
            .ExcludeFromDescription();

        return app;
    }

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

    <section class="search-preview" aria-label="Podgl¹d wyszukiwarki firm">
      <div class="section-head compact">
        <h2>Podgl¹d wyników wyszukiwania</h2>
        <a class="button button-ghost" href="/swagger/index.html#/Companies/get_companies">Testuj endpoint</a>
      </div>
      <div class="table-wrap">
        <table>
          <thead><tr><th>NIP</th><th>Nazwa</th><th>Email</th><th>WWW</th><th>PKD</th><th>Status</th></tr></thead>
          <tbody>
            <tr><td>7312045678</td><td>FIRMA ABC JAN KOWALSKI</td><td>biuro@firmaabc.pl</td><td>firmaabc.pl</td><td>62.01.Z</td><td><span class="status ok">Aktywny</span></td></tr>
            <tr><td>9491832736</td><td>PV SOLUTIONS SPÓ£KA Z O.O.</td><td>kontakt@pvsolutions.pl</td><td>pvsolutions.pl</td><td>43.21.Z</td><td><span class="status ok">Aktywny</span></td></tr>
            <tr><td>8762459076</td><td>SUN ENERGY S.C.</td><td>biuro@sunenergy.pl</td><td>sunenergy.pl</td><td>43.21.Z</td><td><span class="status ok">Aktywny</span></td></tr>
            <tr><td>5223001189</td><td>EKO INSTALACJE DARIUSZ NOWAK</td><td>d.nowak@ekoinstalacje.pl</td><td>ekoinstalacje.pl</td><td>43.21.Z</td><td><span class="status warn">Zawieszony</span></td></tr>
          </tbody>
        </table>
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
</body>
</html>
""";
}

