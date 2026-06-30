-- Tracks which API key consumed tokens.
-- Existing rows remain valid with api_key_id = null.

alter table app.token_ledger
    add column if not exists api_key_id uuid null references app.api_keys(id) on delete set null;

alter table app.api_query_log
    add column if not exists api_key_id uuid null references app.api_keys(id) on delete set null;

create index if not exists ix_token_ledger_api_key_created
    on app.token_ledger(api_key_id, created_at_utc desc);

create index if not exists ix_api_query_log_api_key_created
    on app.api_query_log(api_key_id, created_at_utc desc);
