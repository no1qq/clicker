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