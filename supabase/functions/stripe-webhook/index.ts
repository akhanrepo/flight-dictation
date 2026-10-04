// POST /functions/v1/stripe-webhook  (called by Stripe, not the app)
// Authenticated by the Stripe-Signature header, so gateway JWT checks are off.
import Stripe from "npm:stripe@17";
import { rowFromSubscription, subscriptionRef } from "../_shared/entitlement.ts";
import { adminClient, json, requireEnv, stripeClient, upsertSubscriptionRow } from "../_shared/supabase.ts";

const cryptoProvider = Stripe.createSubtleCryptoProvider();

Deno.serve(async (req) => {
  if (req.method !== "POST") return json({ error: "Method not allowed" }, 405);

  const stripe = stripeClient();
  const signature = req.headers.get("Stripe-Signature");
  if (!signature) return json({ error: "Missing signature" }, 400);

  let event: Stripe.Event;
  try {
    event = await stripe.webhooks.constructEventAsync(
      await req.text(),
      signature,
      requireEnv("STRIPE_WEBHOOK_SECRET"),
      undefined,
      cryptoProvider,
    );
  } catch (err) {
    console.warn("Rejected webhook with a bad signature", err);
    return json({ error: "Bad signature" }, 400);
  }

  const ref = subscriptionRef(event as unknown as Parameters<typeof subscriptionRef>[0]);
  if (!ref) return json({ received: true, ignored: event.type });

  try {
    // Re-read the subscription so a late or out-of-order event can't
    // overwrite newer state with its stale payload.
    const subscription = await stripe.subscriptions.retrieve(ref.subscriptionId);
    const admin = adminClient();

    let accountId = ref.accountId ?? subscription.metadata?.account_id ?? null;
    if (!accountId) {
      const customer = typeof subscription.customer === "string" ? subscription.customer : subscription.customer.id;
      const { data } = await admin
        .from("subscriptions")
        .select("account_id")
        .eq("stripe_customer_id", customer)
        .maybeSingle();
      accountId = data?.account_id ?? null;
    }
    if (!accountId) {
      // Not created through Flight's checkout; nothing to attach it to.
      console.warn(`No account for subscription ${subscription.id}; ignoring ${event.type}`);
      return json({ received: true, ignored: "unknown account" });
    }

    await upsertSubscriptionRow(admin, rowFromSubscription(subscription, accountId));
    return json({ received: true });
  } catch (err) {
    // A 500 makes Stripe retry with backoff, which is what we want for a
    // transient database or API failure.
    console.error(`stripe-webhook failed on ${event.type}`, err);
    return json({ error: "Webhook handling failed" }, 500);
  }
});
