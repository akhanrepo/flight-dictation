// GET /functions/v1/entitlement -> { "entitlement": "paid" | "none" }
// Called by HttpSupabaseClient.GetEntitlementAsync with the user's JWT.
import { entitlementFor } from "../_shared/entitlement.ts";
import { adminClient, getSubscriptionRow, json, requireUser } from "../_shared/supabase.ts";

Deno.serve(async (req) => {
  if (req.method !== "GET") return json({ error: "Method not allowed" }, 405);

  try {
    const admin = adminClient();
    const user = await requireUser(req, admin);
    if (user instanceof Response) return user;

    const row = await getSubscriptionRow(admin, user.id);
    return json({ entitlement: entitlementFor(row, new Date()) });
  } catch (err) {
    console.error("entitlement failed", err);
    return json({ error: "Couldn't read entitlement" }, 500);
  }
});
