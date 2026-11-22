import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { PageHero } from "@/components/page-hero";
import { getPlansAction } from "@/features/plans/actions/plan-actions";
import { getSubscriptionsAction } from "@/features/subscriptions/actions/subscription-actions";
import { PlanTable } from "@/features/plans/components/plan-table";

/**
 * SuperAdmin-only subscription-plan catalog.
 * Protected at four layers: middleware (/superadmin/*), this server guard,
 * the server actions (requiredRole), and the API ([Authorize(Roles="SuperAdmin")]).
 */
export default async function PlansPage() {
  const session = await auth();
  if (session?.user?.role !== "SuperAdmin") {
    redirect("/unauthorized");
  }

  const [plans, subs] = await Promise.all([getPlansAction({ page: 1, pageSize: 200 }), getSubscriptionsAction({ page: 1, pageSize: 200 })]);
  const planList = plans.success ? plans.data.items : null;
  const subList = subs.success ? subs.data.items : null;

  return (
    <div className="flex flex-1 flex-col gap-5 p-4 md:p-6">
      <PageHero
        title="Plans"
        description="The tiers a company can be on: how many messages a month, the price, and which features come with it."
        facts={[
          { label: "Plans", value: planList ? planList.length : null },
          { label: "On sale", value: planList ? planList.filter((p) => p.isActive).length : null, tone: "green" },
          { label: "Companies subscribed", value: subList ? subList.filter((s) => s.status === "Active").length : null },
        ]}
      />

      <PlanTable />
    </div>
  );
}
