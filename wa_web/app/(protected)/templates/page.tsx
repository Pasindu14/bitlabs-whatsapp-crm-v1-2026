import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { PageHero, totalOf } from "@/components/page-hero";
import { getTemplatesAction } from "@/features/templates/actions/template-actions";
import { TemplateTable } from "@/features/templates/components/template-table";
import { StarterPackButton } from "@/features/templates/components/starter-pack-button";

/**
 * CompanyAdmin-only template management. Protected at four layers: proxy (auth), this server
 * guard (role), the server actions (requiredRole), and the API ([Authorize(Roles="CompanyAdmin")]
 * + JWT company scoping). Plan 004.
 */
export default async function TemplatesPage() {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }

  const count = (status: string) => getTemplatesAction({ page: 1, pageSize: 1, status }).then(totalOf);
  const [approved, pending, rejected] = await Promise.all([count("Approved"), count("Pending"), count("Rejected")]);

  return (
    <div className="flex flex-1 flex-col gap-5 p-4 md:p-6">
      <PageHero
        title="Message templates"
        description="Campaigns and messages outside the 24-hour reply window must use a template Meta has approved. Build one here and track its review."
        facts={[
          { label: "Approved", value: approved, tone: "green" },
          { label: "In review", value: pending, tone: "amber" },
          { label: "Rejected", value: rejected, tone: "red" },
        ]}
      >
        <StarterPackButton />
      </PageHero>

      <TemplateTable />
    </div>
  );
}
