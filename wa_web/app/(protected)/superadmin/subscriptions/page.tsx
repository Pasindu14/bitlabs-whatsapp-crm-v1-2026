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
      <div className="rounded-2xl border bg-muted/40 px-8 py-10">
        <h1 className="text-3xl font-bold tracking-tight">Subscriptions</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Assign companies to plans and track their monthly message usage.
        </p>
      </div>

      <SubscriptionTable />
    </div>
  );
}
