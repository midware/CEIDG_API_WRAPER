-- Adds leadbase.network web account confirmation and password reset fields.
-- Safe to run multiple times.

alter table app.api_users
    add column if not exists email_confirmed_at_utc timestamptz null,
    add column if not exists email_confirmation_token_hash text null,
    add column if not exists email_confirmation_expires_at_utc timestamptz null,
    add column if not exists password_reset_token_hash text null,
    add column if not exists password_reset_expires_at_utc timestamptz null,
    add column if not exists last_login_at_utc timestamptz null;

update app.api_users
set email_confirmed_at_utc = coalesce(email_confirmed_at_utc, created_at_utc, now())
where email_confirmed_at_utc is null
  and email_confirmation_token_hash is null;

create index if not exists ix_api_users_email_confirmation_token
    on app.api_users(email_confirmation_token_hash)
    where email_confirmation_token_hash is not null;

create index if not exists ix_api_users_password_reset_token
    on app.api_users(password_reset_token_hash)
    where password_reset_token_hash is not null;
