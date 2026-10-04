import { createClient, type SupabaseClient } from "npm:@supabase/supabase-js@2";
import Stripe from "npm:stripe@17";
import type { SubscriptionRow } from "./entitlement.ts";

export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

export function requireEnv(name: string): string {
  const value = Deno.env.get(name);
  if (!value) throw new Error(`Missing function secret ${name}`);
  return value;
}

/** Service-role client: bypasses RLS, so only ever used server-side. */
export function adminClient(): SupabaseClient {
  // Injected automatically into deployed functions. SUPABASE_SECRET_KEY is
  // the fallback for projects that have disabled the legacy service_role key.
  const key = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") ?? requireEnv("SUPABASE_SECRET_KEY");
  return createClient(requireEnv("SUPABASE_URL"), key, {
    auth: { persistSession: false, autoRefreshToken: false },
  });
}

export function stripeClient(): Stripe {
  return new Stripe(requireEnv("STRIPE_SECRET_KEY"), {
    httpClient: Stripe.createFetchHttpClient(),
  });
}

export interface CallingUser {
  id: string;
  email: string | null;
}

/**
 * Resolves the caller from the `Authorization: Bearer <access token>` header
 * by asking Supabase Auth. Returns a 401 response for a missing or expired
 * token, which the desktop client answers by refreshing its session.
 */
export async function requireUser(req: Request, admin: SupabaseClient): Promise<CallingUser | Response> {
  const token = req.headers.get("Authorization")?.replace(/^Bearer\s+/i, "");
  if (!token) return json({ error: "Missing access token" }, 401);

  const { data, error } = await admin.auth.getUser(token);
  if (error || !data.user) return json({ error: "Invalid or expired access token" }, 401);

  return { id: data.user.id, email: data.user.email ?? null };
}

export async function getSubscriptionRow(admin: SupabaseClient, accountId: string): Promise<SubscriptionRow | null> {
  const { data, error } = await admin
    .from("subscriptions")
    .select("account_id, stripe_customer_id, stripe_subscription_id, status, current_period_end")
    .eq("account_id", accountId)
    .maybeSingle();
  if (error) throw error;
  return data;
}

export async function upsertSubscriptionRow(admin: SupabaseClient, row: Partial<SubscriptionRow> & { account_id: string }) {
  const { error } = await admin
    .from("subscriptions")
    .upsert({ ...row, updated_at: new Date().toISOString() }, { onConflict: "account_id" });
  if (error) throw error;
}

/** Where Stripe sends the browser after checkout or the portal. */
export function returnUrl(): string {
  return Deno.env.get("FLIGHT_RETURN_URL") ?? "https://github.com/akhanrepo/upgraded-octo-flight";
}
