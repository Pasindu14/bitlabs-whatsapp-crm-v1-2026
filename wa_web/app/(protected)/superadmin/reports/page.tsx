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
      <div className="rounded-2xl border bg-muted/40 px-8 py-10">
        <h1 className="text-3xl font-bold tracking-tight">Admin Reports</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Platform-wide reporting — packages purchased by date range, company balances, KPIs, and
          message usage across all tenants.
        </p>
      </div>

      <AdminReportsView />
    </div>
  );
}
