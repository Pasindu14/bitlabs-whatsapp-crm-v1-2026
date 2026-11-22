import { redirect } from "next/navigation";
import { auth } from "@/auth";
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

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      {/* Header card — mirrors the Company Management screen style */}
      <div className="page-header">
        <h1>Plans</h1>
        <p>
          Define the subscription tiers companies can be placed on — quota, price, and features.
        </p>
      </div>

      <PlanTable />
    </div>
  );
}
