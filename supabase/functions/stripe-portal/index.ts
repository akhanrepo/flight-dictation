// POST /functions/v1/stripe-portal -> { "url": "<Stripe hosted billing portal>" }
// Where the user updates payment details or cancels.
import { adminClient, getSubscriptionRow, json, requireUser, returnUrl, stripeClient } from "../_shared/supabase.ts";

Deno.serve(async (req) => {
  if (req.method !== "POST") return json({ error: "Method not allowed" }, 405);

  try {
    const admin = adminClient();
    const user = await requireUser(req, admin);
    if (user instanceof Response) return user;

    const row = await getSubscriptionRow(admin, user.id);
    if (!row?.stripe_customer_id) {
      return json({ error: "There's no subscription on this account yet. Subscribe first." }, 409);
    }

    const session = await stripeClient().billingPortal.sessions.create({
      customer: row.stripe_customer_id,
      return_url: returnUrl(),
    });
    return json({ url: session.url });
  } catch (err) {
    console.error("stripe-portal failed", err);
    return json({ error: "Couldn't open the billing portal" }, 500);
  }
});
