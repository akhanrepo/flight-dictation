// POST /functions/v1/stripe-checkout -> { "url": "<Stripe hosted checkout>" }
// The Stripe secret key stays here; the client only ever sees the URL.
import {
  adminClient,
  getSubscriptionRow,
  json,
  requireEnv,
  requireUser,
  returnUrl,
  stripeClient,
  upsertSubscriptionRow,
} from "../_shared/supabase.ts";

Deno.serve(async (req) => {
  if (req.method !== "POST") return json({ error: "Method not allowed" }, 405);

  try {
    const admin = adminClient();
    const user = await requireUser(req, admin);
    if (user instanceof Response) return user;

    const stripe = stripeClient();
    const existing = await getSubscriptionRow(admin, user.id);

    // One Stripe customer per account, reused across checkouts so the
    // portal shows the whole history.
    let customerId = existing?.stripe_customer_id ?? null;
    if (!customerId) {
      const customer = await stripe.customers.create({
        email: user.email ?? undefined,
        metadata: { account_id: user.id },
      });
      customerId = customer.id;
      await upsertSubscriptionRow(admin, { account_id: user.id, stripe_customer_id: customerId });
    }

    const session = await stripe.checkout.sessions.create({
      mode: "subscription",
      customer: customerId,
      client_reference_id: user.id,
      line_items: [{ price: requireEnv("STRIPE_PRICE_ID"), quantity: 1 }],
      // Copied onto the subscription so every later webhook names the account.
      subscription_data: { metadata: { account_id: user.id } },
      success_url: returnUrl(),
      cancel_url: returnUrl(),
    });

    if (!session.url) return json({ error: "Stripe returned no checkout URL" }, 502);
    return json({ url: session.url });
  } catch (err) {
    console.error("stripe-checkout failed", err);
    return json({ error: "Couldn't start checkout" }, 500);
  }
});
