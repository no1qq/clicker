create extension if not exists pgcrypto;

alter table accounts enable row level security;
alter table click_records enable row level security;

drop policy if exists "accounts_access" on accounts;
create policy "accounts_access" on accounts for all using (true) with check (true);

drop policy if exists "click_records_access" on click_records;
create policy "click_records_access" on click_records for all using (true) with check (true);

grant all on table accounts to anon, authenticated, service_role;
grant all on table click_records to anon, authenticated, service_role;
grant all on all sequences in schema public to anon, authenticated, service_role;

insert into accounts (username, pin_hash)
values (
    'zolsi',
    encode(sha256('372933'::bytea), 'hex')
)
on conflict (username) do update
set pin_hash = excluded.pin_hash,
    hwid = null;