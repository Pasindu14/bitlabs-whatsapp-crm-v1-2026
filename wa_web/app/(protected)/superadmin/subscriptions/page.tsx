import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { PageHero } from "@/components/page-hero";
import { getSubscriptionsAction } from "@/features/subscriptions/actions/subscription-actions";
import { SubscriptionTable } from "@/features/subscriptions/components/subscription-table";

/**
 * SuperAdmin-only subscription management — place companies on plans (manual, no Stripe).
 * Protected at four layers: middleware (/superadmin/*), this server guard,
 * the server actions (requiredRole), and the API ([Authorize(Roles="SuperAdmin")]).
 */
/** Active subscriptions whose period ends within the next `days` days. */
function renewingWithinDays(list: { status: string | null; currentPeriodEnd: string | null }[], days: number): number {
  const cutoff = Date.now() + days * 24 * 60 * 60 * 1000;
  return list.filter(
    (s) => s.status === "Active" && s.currentPeriodEnd !== null && new Date(s.currentPeriodEnd).getTime() <= cutoff,
  ).length;
}

export default async function SubscriptionsPage() {
  const session = await auth();
  if (session?.user?.role !== "SuperAdmin") {
    redirect("/unauthorized");
  }

  const res = await getSubscriptionsAction({ page: 1, pageSize: 200 });
  const list = res.success ? res.data.items : null;
  const endingSoon = list ? renewingWithinDays(list, 7) : null;

  return (
    <div className="flex flex-1 flex-col gap-5 p-4 md:p-6">
      <PageHero
        title="Subscriptions"
        description="Which plan each company is on, how much of it they've used this period, and when it renews."
        facts={[
          { label: "Subscriptions", value: list ? list.length : null },
          { label: "Active", value: list ? list.filter((s) => s.status === "Active").length : null, tone: "green" },
          { label: "Renewing within 7 days", value: endingSoon, tone: "amber" },
        ]}
      />

      <SubscriptionTable />
    </div>
  );
}
