using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace CeidgMirror.Api;

public sealed class LeadbaseEmailOptions
{
    public const string SectionName = "LeadbaseEmail";
    public string PublicBaseUrl { get; init; } = "http://localhost:5075";
    public string FromEmail { get; init; } = "no-reply@leadbase.network";
    public string FromName { get; init; } = "leadbase.network";
    public string? SmtpHost { get; init; }
    public int SmtpPort { get; init; } = 587;
    public string? SmtpUser { get; init; }
    public string? SmtpPassword { get; init; }
    public bool EnableSsl { get; init; } = true;
}

public interface ILeadbaseEmailSender
{
    Task SendConfirmationEmailAsync(string email, string confirmationUrl, CancellationToken cancellationToken);
}

public sealed class LeadbaseEmailSender(LeadbaseEmailOptions options, ILogger<LeadbaseEmailSender> logger) : ILeadbaseEmailSender
{
    public async Task SendConfirmationEmailAsync(string email, string confirmationUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.SmtpHost))
        {
            logger.LogWarning("Leadbase email confirmation for {Email}: {ConfirmationUrl}", email, confirmationUrl);
            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(options.FromEmail, options.FromName),
            Subject = "Potwierdz adres email w leadbase.network",
            Body = $"Kliknij link, aby potwierdzic konto leadbase.network:\n\n{confirmationUrl}",
            IsBodyHtml = false
        };
        message.To.Add(email);

        using var client = new SmtpClient(options.SmtpHost, options.SmtpPort)
        {
            EnableSsl = options.EnableSsl
        };

        if (!string.IsNullOrWhiteSpace(options.SmtpUser))
        {
            client.Credentials = new NetworkCredential(options.SmtpUser, options.SmtpPassword);
        }

        await client.SendMailAsync(message, cancellationToken);
    }
}

public static class LeadbaseAccountEndpoints
{
    public static IServiceCollection AddLeadbaseAccountEmail(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(LeadbaseEmailOptions.SectionName).Get<LeadbaseEmailOptions>() ?? new LeadbaseEmailOptions();
        services.AddSingleton(options);
        services.AddSingleton<ILeadbaseEmailSender, LeadbaseEmailSender>();
        return services;
    }

    public static WebApplication MapLeadbaseAccount(this WebApplication app)
    {
        app.MapGet("/register", () => Results.Content(RegisterHtml(null, null), "text/html; charset=utf-8"))
            .ExcludeFromDescription();

        app.MapPost("/register", async (HttpContext context, ProductApiStore store, ProductApiOptions productOptions, LeadbaseEmailOptions emailOptions, ILeadbaseEmailSender emailSender, CancellationToken cancellationToken) =>
        {
            var form = await context.Request.ReadFormAsync(cancellationToken);
            var email = form["email"].ToString().Trim();
            var password = form["password"].ToString();
            var displayName = form["displayName"].ToString().Trim();
            var normalizedEmail = NormalizeEmail(email);

            if (normalizedEmail is null)
            {
                return Results.Content(RegisterHtml("Podaj poprawny adres email.", email), "text/html; charset=utf-8");
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 10)
            {
                return Results.Content(RegisterHtml("Haslo musi miec minimum 10 znakow.", email), "text/html; charset=utf-8");
            }

            var confirmationToken = AccountTokenSecurity.GenerateToken();
            try
            {
                await store.RegisterUserAsync(
                    normalizedEmail,
                    email,
                    string.IsNullOrWhiteSpace(displayName) ? null : displayName,
                    PasswordSecurity.HashPassword(password),
                    productOptions.FreeRegistrationTokens,
                    AccountTokenSecurity.HashToken(confirmationToken),
                    DateTimeOffset.UtcNow.AddHours(24),
                    cancellationToken);
            }
            catch (DuplicateEmailException)
            {
                return Results.Content(RegisterHtml("Konto z tym adresem email juz istnieje.", email), "text/html; charset=utf-8");
            }

            var confirmationUrl = BuildConfirmationUrl(emailOptions.PublicBaseUrl, confirmationToken);
            await emailSender.SendConfirmationEmailAsync(email, confirmationUrl, cancellationToken);
            return Results.Content(CheckEmailHtml(email, confirmationUrl), "text/html; charset=utf-8");
        })
        .DisableAntiforgery()
        .ExcludeFromDescription();

        app.MapGet("/confirm-email", async (string? token, ProductApiStore store, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return Results.Content(MessageHtml("Niepoprawny link", "Brakuje tokenu potwierdzajacego email.", "/register", "Utworz konto"), "text/html; charset=utf-8");
            }

            var confirmed = await store.ConfirmEmailAsync(AccountTokenSecurity.HashToken(token), cancellationToken);
            return confirmed
                ? Results.Content(MessageHtml("Email potwierdzony", "Mozesz sie teraz zalogowac i korzystac z panelu leadbase.network.", "/login", "Zaloguj sie"), "text/html; charset=utf-8")
                : Results.Content(MessageHtml("Link jest niewazny", "Token wygasl albo zostal juz wykorzystany.", "/register", "Utworz konto"), "text/html; charset=utf-8");
        })
        .ExcludeFromDescription();

        app.MapGet("/login", () => Results.Content(LoginHtml(null, null), "text/html; charset=utf-8"))
            .ExcludeFromDescription();

        app.MapPost("/login", async (HttpContext context, ProductApiStore store, CancellationToken cancellationToken) =>
        {
            var form = await context.Request.ReadFormAsync(cancellationToken);
            var email = form["email"].ToString().Trim();
            var password = form["password"].ToString();
            var normalizedEmail = NormalizeEmail(email);

            if (normalizedEmail is null || string.IsNullOrWhiteSpace(password))
            {
                return Results.Content(LoginHtml("Podaj email i haslo.", email), "text/html; charset=utf-8");
            }

            var user = await store.GetUserForLoginAsync(normalizedEmail, cancellationToken);
            if (user is null || !PasswordSecurity.VerifyPassword(password, user.PasswordHash))
            {
                return Results.Content(LoginHtml("Niepoprawny email lub haslo.", email), "text/html; charset=utf-8");
            }

            if (!user.EmailConfirmed)
            {
                return Results.Content(LoginHtml("Najpierw potwierdz adres email.", email), "text/html; charset=utf-8");
            }

            await store.MarkLoginAsync(user.UserId, cancellationToken);
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new(ClaimTypes.Email, email),
                new(ClaimTypes.Name, email)
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            return Results.Redirect("/app");
        })
        .DisableAntiforgery()
        .ExcludeFromDescription();

        app.MapGet("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/");
        })
        .ExcludeFromDescription();

        return app;
    }

    private static string? NormalizeEmail(string? email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToUpperInvariant();

    private static string BuildConfirmationUrl(string publicBaseUrl, string token) =>
        publicBaseUrl.TrimEnd('/') + "/confirm-email?token=" + Uri.EscapeDataString(token);

    private static string RegisterHtml(string? error, string? email) => AccountShell("Utworz konto", $"""
        <form class="account-form" method="post" action="/register">
          <h1>Utworz konto</h1>
          <p>Odbierz startowe tokeny i testuj realne endpointy leadbase.network.</p>
          {Error(error)}
          <label>Nazwa lub imie<input name="displayName" autocomplete="name"></label>
          <label>Email<input name="email" type="email" autocomplete="email" value="{Html(email)}" required></label>
          <label>Haslo<input name="password" type="password" autocomplete="new-password" minlength="10" required></label>
          <button class="button button-primary button-large" type="submit">Zarejestruj konto</button>
          <a href="/login">Mam juz konto</a>
        </form>
        """);

    private static string LoginHtml(string? error, string? email) => AccountShell("Logowanie", $"""
        <form class="account-form" method="post" action="/login">
          <h1>Zaloguj sie</h1>
          <p>Wejdz do panelu, sprawdz saldo tokenow i zarzadzaj kluczami API.</p>
          {Error(error)}
          <label>Email<input name="email" type="email" autocomplete="email" value="{Html(email)}" required></label>
          <label>Haslo<input name="password" type="password" autocomplete="current-password" required></label>
          <button class="button button-primary button-large" type="submit">Zaloguj sie</button>
          <a href="/register">Utworz nowe konto</a>
        </form>
        """);

    private static string CheckEmailHtml(string email, string confirmationUrl) => AccountShell("Potwierdz email", $"""
        <div class="account-form">
          <h1>Sprawdz skrzynke email</h1>
          <p>Wyslalismy link potwierdzajacy na <strong>{Html(email)}</strong>. Konto zacznie dzialac po potwierdzeniu adresu.</p>
          <p class="dev-link">Dev link: <a href="{Html(confirmationUrl)}">potwierdz konto</a></p>
          <a class="button button-primary button-large" href="/login">Przejdz do logowania</a>
        </div>
        """);

    private static string MessageHtml(string title, string body, string href, string cta) => AccountShell(title, $"""
        <div class="account-form">
          <h1>{Html(title)}</h1>
          <p>{Html(body)}</p>
          <a class="button button-primary button-large" href="{Html(href)}">{Html(cta)}</a>
        </div>
        """);

    private static string AccountShell(string title, string content) => $"""
<!doctype html>
<html lang="pl">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{Html(title)} - leadbase.network</title>
  <link rel="stylesheet" href="/leadbase.css">
</head>
<body class="account-page">
  <main class="account-shell">
    <a class="brand" href="/"><span class="brand-mark">lb</span><span>leadbase.network</span></a>
    {content}
  </main>
</body>
</html>
""";

    private static string Error(string? error) => string.IsNullOrWhiteSpace(error) ? string.Empty : $"<div class=\"form-error\">{Html(error)}</div>";
    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

public static class AccountTokenSecurity
{
    public static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string HashToken(string token) => ApiKeySecurity.HashApiKey(token);
}
