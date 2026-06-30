-- Adds optional expiration timestamp for API keys.
-- Safe to run multiple times.

alter table app.api_keys
    add column if not exists expires_at_utc timestamptz null;

drop index if exists ix_api_keys_active_hash;
create index if not exists ix_api_keys_active_hash
    on app.api_keys(key_hash)
    where revoked_at_utc is null;

create index if not exists ix_api_keys_user_status
    on app.api_keys(user_id, revoked_at_utc, expires_at_utc, created_at_utc desc);
