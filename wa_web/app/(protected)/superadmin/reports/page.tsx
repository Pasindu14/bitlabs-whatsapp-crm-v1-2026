import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { AdminReportsView } from "@/features/admin-reports/components/admin-reports-view";

export default async function ReportsPage() {
  const session = await auth();
  if (session?.user?.role !== "SuperAdmin") {
    redirect("/unauthorized");
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      <div className="page-header">
        <h1>Admin Reports</h1>
        <p>
          Platform-wide reporting — packages purchased by date range, company balances, KPIs, and
          message usage across all tenants.
        </p>
      </div>

      <AdminReportsView />
    </div>
  );
}
