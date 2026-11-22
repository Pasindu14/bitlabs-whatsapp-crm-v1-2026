import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { SubscriptionTable } from "@/features/subscriptions/components/subscription-table";

/**
 * SuperAdmin-only subscription management — place companies on plans (manual, no Stripe).
 * Protected at four layers: middleware (/superadmin/*), this server guard,
 * the server actions (requiredRole), and the API ([Authorize(Roles="SuperAdmin")]).
 */
export default async function SubscriptionsPage() {
  const session = await auth();
  if (session?.user?.role !== "SuperAdmin") {
    redirect("/unauthorized");
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      <div className="page-header">
        <h1>Subscriptions</h1>
        <p>
          Assign companies to plans and track their monthly message usage.
        </p>
      </div>

      <SubscriptionTable />
    </div>
  );
}
