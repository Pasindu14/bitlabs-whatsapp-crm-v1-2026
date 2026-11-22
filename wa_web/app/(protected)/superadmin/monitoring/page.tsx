import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { PageHero } from "@/components/page-hero";
import { getCompanyHealthAction } from "@/features/monitoring/actions/monitoring-actions";
import { CompanyHealthTable } from "@/features/monitoring/components/company-health-table";

export default async function MonitoringPage() {
  const session = await auth();
  if (session?.user?.role !== "SuperAdmin") {
    redirect("/unauthorized");
  }

  const res = await getCompanyHealthAction();
  const list = res.success ? res.data.companies : null;
  const sent = list ? list.reduce((t, c) => t + c.totalSent30d, 0) : null;

  return (
    <div className="flex flex-1 flex-col gap-5 p-4 md:p-6">
      <PageHero
        title="Platform monitoring"
        description="How every company's messaging went over the last 30 days. A company is flagged when more than 1 in 10 of its messages fail."
        facts={[
          { label: "Messages sent", value: sent },
          { label: "Companies sending campaigns now", value: list ? list.filter((c) => c.activeCampaigns > 0).length : null, tone: "blue" },
          { label: "Flagged", value: list ? list.filter((c) => c.isFlagged).length : null, tone: "red" },
        ]}
      />

      <CompanyHealthTable />
    </div>
  );
}
