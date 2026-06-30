# leadbase.network Product Roadmap

This document splits the product build into controlled stages. The current codebase already has CEIDG mirroring, PostgreSQL storage, token-billed API endpoints, Swagger and the first product website.

## Stage 1 - Public Product Website And Endpoint Tester

Status: in progress.

Scope:
- Product landing page under `/`.
- Swagger under `/swagger`.
- Graphical endpoint tester on the website.
- Anonymous demo endpoint limited to 2 calls.
- Full selectable column list aligned with the backend `CompanyColumnCatalog`.
- Registered users can paste an API key and call the real `/companies` endpoint from the tester.

Done:
- Website and first tester UI exist.
- Demo limit is enforced server-side and mirrored client-side.
- Tester exposes contact, address, owner, PKD and raw payload columns.

## Stage 2 - Account Registration And Login

Scope:
- Replace Swagger-only registration with product-native web registration and login screens.
- Add email confirmation before the account becomes fully active.
- Add resend confirmation email flow.
- Add password reset flow.
- Add secure session/cookie login for the web panel.
- Keep API-key authentication for API calls.

FitExpertCRM reference:
- Use `expertfit/FitExpertCRM` as the functional reference for registration, account creation and login UX/backend flow.
- Before implementation, inspect its auth modules, email confirmation token model, password reset flow, validation rules, mail templates and account activation states.
- Do not copy secrets, environment-specific config or branding from FitExpertCRM.

Database work:
- Extend `app.api_users` with `email_confirmed_at_utc`, `email_confirmation_token_hash`, `email_confirmation_expires_at_utc`, `password_reset_token_hash`, `password_reset_expires_at_utc`, `last_login_at_utc`.
- Add migration file under `db/migrations`.

## Stage 3 - User Panel

Scope:
- `/app` becomes a real authenticated account panel.
- Show token balance, token ledger, query history and API keys.
- Add API key creation, naming, revocation and last-used display.
- Show pricing/package options.

## Stage 4 - Billing And Token Purchases

Scope:
- Payment provider integration.
- Token packages, purchase history and invoices.
- Atomic token crediting through `app.token_ledger`.
- Admin/manual token top-up path.

## Stage 5 - CRM Foundation

Scope:
- Saved company lists.
- Lead statuses, tags, notes and ownership.
- Import selected API search results into lead lists.
- User/team model if needed.

FitExpertCRM reference:
- Copy only validated domain patterns that fit leadbase.network: contacts, notes, pipeline/list concepts and user account ergonomics.

## Stage 6 - Email And SMS Campaigns

Scope:
- Campaign lists built from saved leads.
- Email templates and sending provider integration.
- SMS templates and sending provider integration.
- Rate limits, opt-out handling, consent/audit logs.
- Campaign analytics and retry handling.

Important sequencing:
- Do not start campaign features before account auth, email confirmation, user panel and billing are stable.
- Campaigns must be designed around compliance and deliverability from the start.
