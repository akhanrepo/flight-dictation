// Pure entitlement and subscription-mapping logic, kept free of network and
// npm imports so `deno test` runs it offline.

/** Matches `RemoteEntitlements` in Flight.Core. */
export type Entitlement = "paid" | "none";

export interface SubscriptionRow {
  account_id: string;
  stripe_customer_id: string | null;
  stripe_subscription_id: string | null;
  status: string | null;
  current_period_end: string | null;
}

// past_due keeps access while Stripe retries the card (its hosted dunning
// handles the rest); everything else -- incomplete, unpaid, canceled,
// paused -- reads as no subscription.
const PAID_STATUSES = new Set(["active", "trialing", "past_due"]);

export function entitlementFor(
  row: Pick<SubscriptionRow, "status" | "current_period_end"> | null,
  now: Date,
): Entitlement {
  if (!row?.status || !PAID_STATUSES.has(row.status)) return "none";
  if (row.current_period_end && new Date(row.current_period_end) <= now) return "none";
  return "paid";
}

/** The subset of a Stripe Subscription this backend reads. */
export interface StripeSubscriptionLike {
  id: string;
  status: string;
  customer: string | { id: string };
  metadata?: Record<string, string> | null;
  // Pre-2025-03-31 API versions carry the period on the subscription; newer
  // ones moved it onto each subscription item. Accept both.
  current_period_end?: number | null;
  items?: { data?: Array<{ id?: string; current_period_end?: number | null }> } | null;
}

export function customerId(customer: string | { id: string }): string {
  return typeof customer === "string" ? customer : customer.id;
}

export function rowFromSubscription(sub: StripeSubscriptionLike, accountId: string): SubscriptionRow {
  const periodEnd = sub.current_period_end ?? sub.items?.data?.[0]?.current_period_end ?? null;
  return {
    account_id: accountId,
    stripe_customer_id: customerId(sub.customer),
    stripe_subscription_id: sub.id,
    status: sub.status,
    current_period_end: periodEnd ? new Date(periodEnd * 1000).toISOString() : null,
  };
}

/** The subset of a Stripe Event this backend reads. */
export interface StripeEventLike {
  type: string;
  data: { object: Record<string, unknown> };
}

export const HANDLED_EVENTS = new Set([
  "checkout.session.completed",
  "customer.subscription.created",
  "customer.subscription.updated",
  "customer.subscription.deleted",
]);

/**
 * Which subscription an event concerns, plus the account id when the event
 * carries one. The webhook re-reads the subscription from Stripe rather than
 * trusting the payload, so out-of-order delivery can't regress the row.
 */
export function subscriptionRef(
  event: StripeEventLike,
): { subscriptionId: string; accountId: string | null } | null {
  if (!HANDLED_EVENTS.has(event.type)) return null;
  const obj = event.data.object;

  if (event.type === "checkout.session.completed") {
    const sub = obj.subscription;
    const subscriptionId = typeof sub === "string" ? sub : (sub as { id?: string } | null)?.id;
    if (!subscriptionId) return null;
    return { subscriptionId, accountId: (obj.client_reference_id as string | null) ?? null };
  }

  const metadata = obj.metadata as Record<string, string> | null | undefined;
  return { subscriptionId: obj.id as string, accountId: metadata?.account_id ?? null };
}
