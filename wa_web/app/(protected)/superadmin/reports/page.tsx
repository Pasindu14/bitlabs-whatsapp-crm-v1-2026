import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { PageHero } from "@/components/page-hero";
import { AdminReportsView } from "@/features/admin-reports/components/admin-reports-view";

export default async function ReportsPage() {
  const session = await auth();
  if (session?.user?.role !== "SuperAdmin") {
    redirect("/unauthorized");
  }


  return (
    <div className="flex flex-1 flex-col gap-5 p-4 md:p-6">
      <PageHero
        title="Admin reports"
        description="Platform-wide numbers: packages sold in a date range, each company's balance, and message and AI usage across every company."
      />

      <AdminReportsView />
    </div>
  );
}
