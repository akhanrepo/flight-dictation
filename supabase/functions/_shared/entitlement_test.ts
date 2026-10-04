import { assertEquals } from "jsr:@std/assert@1";
import { entitlementFor, rowFromSubscription, subscriptionRef } from "./entitlement.ts";

const now = new Date("2026-10-04T12:00:00Z");
const future = "2026-11-04T12:00:00Z";
const past = "2026-10-01T12:00:00Z";

Deno.test("no subscription row is none", () => {
  assertEquals(entitlementFor(null, now), "none");
});

Deno.test("customer created at checkout but not yet subscribed is none", () => {
  assertEquals(entitlementFor({ status: null, current_period_end: null }, now), "none");
});

Deno.test("active, trialing and past_due within the period are paid", () => {
  for (const status of ["active", "trialing", "past_due"]) {
    assertEquals(entitlementFor({ status, current_period_end: future }, now), "paid", status);
  }
});

// Feature #5: cancelling in Stripe's portal reverts cloud access on next sync
Deno.test("canceled is none", () => {
  assertEquals(entitlementFor({ status: "canceled", current_period_end: future }, now), "none");
});

Deno.test("incomplete, unpaid and paused are none", () => {
  for (const status of ["incomplete", "incomplete_expired", "unpaid", "paused"]) {
    assertEquals(entitlementFor({ status, current_period_end: future }, now), "none", status);
  }
});

// A missed deletion webhook mustn't grant access forever
Deno.test("active but past the period end is none", () => {
  assertEquals(entitlementFor({ status: "active", current_period_end: past }, now), "none");
});

Deno.test("rowFromSubscription reads the legacy subscription-level period", () => {
  const row = rowFromSubscription(
    { id: "sub_1", status: "active", customer: "cus_1", current_period_end: 1_790_000_000 },
    "acct-1",
  );
  assertEquals(row, {
    account_id: "acct-1",
    stripe_customer_id: "cus_1",
    stripe_subscription_id: "sub_1",
    status: "active",
    current_period_end: new Date(1_790_000_000 * 1000).toISOString(),
  });
});

Deno.test("rowFromSubscription reads the item-level period of newer API versions", () => {
  const row = rowFromSubscription(
    { id: "sub_1", status: "active", customer: { id: "cus_1" }, items: { data: [{ current_period_end: 1_790_000_000 }] } },
    "acct-1",
  );
  assertEquals(row.stripe_customer_id, "cus_1");
  assertEquals(row.current_period_end, new Date(1_790_000_000 * 1000).toISOString());
});

Deno.test("checkout completion maps to its subscription and client_reference_id", () => {
  const ref = subscriptionRef({
    type: "checkout.session.completed",
    data: { object: { subscription: "sub_1", client_reference_id: "acct-1" } },
  });
  assertEquals(ref, { subscriptionId: "sub_1", accountId: "acct-1" });
});

Deno.test("subscription events map via metadata.account_id", () => {
  for (const type of ["customer.subscription.created", "customer.subscription.updated", "customer.subscription.deleted"]) {
    const ref = subscriptionRef({ type, data: { object: { id: "sub_1", metadata: { account_id: "acct-1" } } } });
    assertEquals(ref, { subscriptionId: "sub_1", accountId: "acct-1" }, type);
  }
});

Deno.test("subscription events without metadata still name the subscription", () => {
  const ref = subscriptionRef({ type: "customer.subscription.updated", data: { object: { id: "sub_1" } } });
  assertEquals(ref, { subscriptionId: "sub_1", accountId: null });
});

Deno.test("unrelated events are ignored", () => {
  assertEquals(subscriptionRef({ type: "invoice.paid", data: { object: { id: "in_1" } } }), null);
});

Deno.test("one-off checkout with no subscription is ignored", () => {
  assertEquals(
    subscriptionRef({ type: "checkout.session.completed", data: { object: { subscription: null } } }),
    null,
  );
});
