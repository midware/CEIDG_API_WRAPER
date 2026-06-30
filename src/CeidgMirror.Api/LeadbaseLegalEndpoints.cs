using System.Text;

namespace CeidgMirror.Api;

public static class LeadbaseLegalEndpoints
{
    private static readonly IReadOnlyDictionary<string, LegalDocument> Documents = new Dictionary<string, LegalDocument>(StringComparer.OrdinalIgnoreCase)
    {
        ["/terms"] = new("Regulamin", "REGULAMIN_PL.md", "Zasady korzystania z API, kont, tokenow i danych."),
        ["/privacy"] = new("Polityka prywatnosci", "PRIVACY_POLICY_PL.md", "Informacje RODO dla uzytkownikow platformy i osob z bazy."),
        ["/cookies"] = new("Polityka cookies", "COOKIES_POLICY_PL.md", "Cookies, local storage i technologie podobne."),
        ["/opt-out"] = new("Prawa osob i opt-out", "DATA_RIGHTS_AND_OPT_OUT_PL.md", "Sprzeciw, usuniecie, sprostowanie i ograniczenie danych."),
        ["/security"] = new("Bezpieczenstwo", "SECURITY_PL.md", "Podstawowe zabezpieczenia techniczne i organizacyjne."),
        ["/dpa"] = new("DPA", "DPA_PL.md", "Powierzenie przetwarzania dla funkcji upload, CRM i kampanii.")
    };

    public static WebApplication MapLeadbaseLegal(this WebApplication app)
    {
        app.MapGet("/legal", () => Results.Content(RenderLegalIndex(), "text/html; charset=utf-8"))
            .ExcludeFromDescription();

        foreach (var (route, document) in Documents)
        {
            app.MapGet(route, () => Results.Content(RenderLegalDocument(document), "text/html; charset=utf-8"))
                .ExcludeFromDescription();
        }

        return app;
    }

    private static string RenderLegalIndex()
    {
        var links = string.Join(string.Empty, Documents.Select(item => $"""
          <a class="legal-card" href="{item.Key}">
            <strong>{Html(item.Value.Title)}</strong>
            <span>{Html(item.Value.Description)}</span>
          </a>
"""));

        return LegalShell("Centrum prawne", $"""
        <section class="legal-hero">
          <p class="eyebrow">leadbase.network</p>
          <h1>Centrum prawne</h1>
          <p>Dokumenty regulujace korzystanie z platformy, API, tokenow, danych CEIDG oraz procedury prywatnosci i bezpieczenstwa.</p>
        </section>
        <section class="legal-grid">
{links}
        </section>
        <section class="legal-note">
          <strong>Wazne:</strong> dokumenty sa wdrozonymi wersjami roboczymi. Przed publikacja produkcyjna nalezy uzupelnic dane operatora, dostawcow i retencje oraz wykonac przeglad prawny.
        </section>
""");
    }

    private static string RenderLegalDocument(LegalDocument document)
    {
        var markdown = ReadLegalMarkdown(document.FileName);
        var body = MarkdownToHtml(markdown);
        return LegalShell(document.Title, $"""
        <a class="legal-back" href="/legal">Centrum prawne</a>
        <article class="legal-document">
{body}
        </article>
""");
    }

    private static string LegalShell(string title, string body) => $"""
<!doctype html>
<html lang="pl">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{Html(title)} - leadbase.network</title>
  <link rel="stylesheet" href="/leadbase.css">
</head>
<body>
  <header class="site-header">
    <a class="brand" href="/"><span class="brand-mark">lb</span><span>leadbase.network</span></a>
    <nav class="nav"><a href="/">Strona glowna</a><a href="/legal">Prawo</a><a href="/swagger">API</a></nav>
    <div class="header-actions"><a class="button button-ghost" href="/login">Logowanie</a><a class="button button-primary" href="/register">Utworz konto</a></div>
  </header>
  <main class="legal-shell">
{body}
  </main>
  <footer class="footer footer-legal">
    <span>leadbase.network</span>
    <a href="/terms">Regulamin</a>
    <a href="/privacy">Prywatnosc</a>
    <a href="/cookies">Cookies</a>
    <a href="/opt-out">Opt-out</a>
    <a href="/security">Bezpieczenstwo</a>
  </footer>
</body>
</html>
""";

    private static string ReadLegalMarkdown(string fileName)
    {
        var legalDirectory = FindLegalDirectory();
        var path = Path.Combine(legalDirectory, fileName);
        return File.Exists(path)
            ? File.ReadAllText(path, Encoding.UTF8)
            : "# Dokument niedostepny\n\nNie znaleziono pliku dokumentu prawnego.";
    }

    private static string FindLegalDirectory()
    {
        var candidates = new List<string>();
        AddWalkUpCandidates(candidates, Directory.GetCurrentDirectory());
        AddWalkUpCandidates(candidates, AppContext.BaseDirectory);

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var legalDirectory = Path.Combine(candidate, "docs", "legal");
            if (Directory.Exists(legalDirectory))
            {
                return legalDirectory;
            }
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "docs", "legal");
    }

    private static void AddWalkUpCandidates(List<string> candidates, string start)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            candidates.Add(directory.FullName);
            directory = directory.Parent;
        }
    }

    private static string MarkdownToHtml(string markdown)
    {
        var html = new StringBuilder();
        var inList = false;
        var inTable = false;
        var tableHasHeader = false;

        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Wersja robocza:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Status:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                CloseList();
                CloseTable();
                continue;
            }

            if (trimmed.StartsWith("|", StringComparison.Ordinal) && trimmed.EndsWith("|", StringComparison.Ordinal))
            {
                CloseList();
                if (trimmed.Contains("---", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!inTable)
                {
                    html.AppendLine("<div class=\"legal-table-wrap\"><table class=\"legal-table\">");
                    inTable = true;
                    tableHasHeader = false;
                }

                var cells = trimmed.Trim('|').Split('|').Select(cell => Html(cell.Trim())).ToArray();
                var tag = tableHasHeader ? "td" : "th";
                html.Append("<tr>");
                foreach (var cell in cells)
                {
                    html.Append('<').Append(tag).Append('>').Append(cell).Append("</").Append(tag).Append('>');
                }

                html.AppendLine("</tr>");
                tableHasHeader = true;
                continue;
            }

            CloseTable();

            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                CloseList();
                html.AppendLine($"<h1>{Html(trimmed[2..])}</h1>");
            }
            else if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                CloseList();
                html.AppendLine($"<h2>{Html(trimmed[3..])}</h2>");
            }
            else if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                CloseList();
                html.AppendLine($"<h3>{Html(trimmed[4..])}</h3>");
            }
            else if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                if (!inList)
                {
                    html.AppendLine("<ul>");
                    inList = true;
                }

                html.AppendLine($"<li>{Html(trimmed[2..])}</li>");
            }
            else
            {
                CloseList();
                html.AppendLine($"<p>{Html(trimmed)}</p>");
            }
        }

        CloseList();
        CloseTable();
        return html.ToString();

        void CloseList()
        {
            if (!inList)
            {
                return;
            }

            html.AppendLine("</ul>");
            inList = false;
        }

        void CloseTable()
        {
            if (!inTable)
            {
                return;
            }

            html.AppendLine("</table></div>");
            inTable = false;
        }
    }

    private static string Html(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    private sealed record LegalDocument(string Title, string FileName, string Description);
}
