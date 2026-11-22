import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { CompanyHealthTable } from "@/features/monitoring/components/company-health-table";

export default async function MonitoringPage() {
  const session = await auth();
  if (session?.user?.role !== "SuperAdmin") {
    redirect("/unauthorized");
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      <div className="page-header">
        <h1>Platform Monitoring</h1>
        <p>
          Cross-tenant health overview — message volumes, failure rates, and active campaigns per company (last 30 days). Companies with a failure rate above 10% are flagged.
        </p>
      </div>

      <CompanyHealthTable />
    </div>
  );
}
