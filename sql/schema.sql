create extension if not exists pgcrypto;

create table if not exists accounts (
    id uuid default gen_random_uuid() primary key,
    username text unique not null,
    pin_hash text not null,
    hwid text,
    config text,
    created_at timestamptz default now()
);

alter table accounts add column if not exists config text;

create table if not exists click_records (
    id uuid default gen_random_uuid() primary key,
    user_id uuid references accounts(id) on delete cascade not null,
    name text not null,
    data text not null,
    cps numeric(5,2) not null default 0,
    created_at timestamptz default now(),
    updated_at timestamptz default now()
);

alter table accounts enable row level security;
alter table click_records enable row level security;

drop policy if exists "accounts_access" on accounts;
create policy "accounts_access" on accounts for all using (true) with check (true);

drop policy if exists "click_records_access" on click_records;
create policy "click_records_access" on click_records for all using (true) with check (true);

grant all on table accounts to anon, authenticated, service_role;
grant all on table click_records to anon, authenticated, service_role;
grant all on all sequences in schema public to anon, authenticated, service_role;

create table if not exists blacklisted_builds (
    id uuid default gen_random_uuid() primary key,
    hash text unique not null,
    notes text,
    blacklisted_at timestamptz default now()
);

alter table blacklisted_builds enable row level security;

drop policy if exists "blacklisted_builds_access" on blacklisted_builds;
create policy "blacklisted_builds_access" on blacklisted_builds for all using (true) with check (true);

grant all on table blacklisted_builds to anon, authenticated, service_role;