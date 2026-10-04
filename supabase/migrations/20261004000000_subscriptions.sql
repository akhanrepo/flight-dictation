-- One row per account: its Stripe customer and the latest known state of its
-- subscription. Written only by Edge Functions (service role) from Stripe
-- webhooks; the desktop client never reads this table directly, it asks the
-- `entitlement` function.
create table public.subscriptions (
  account_id             uuid primary key references auth.users (id) on delete cascade,
  stripe_customer_id     text unique,
  stripe_subscription_id text,
  status                 text,
  current_period_end     timestamptz,
  updated_at             timestamptz not null default now()
);

alter table public.subscriptions enable row level security;

-- Read-own only. No insert/update/delete policies: with RLS on, only the
-- service role can write.
create policy "Users read their own subscription"
  on public.subscriptions
  for select
  to authenticated
  using ((select auth.uid()) = account_id);
