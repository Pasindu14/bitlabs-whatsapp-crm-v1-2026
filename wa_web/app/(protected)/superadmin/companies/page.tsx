import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { PageHero, totalOf } from "@/components/page-hero";
import { getCompaniesAction } from "@/features/companies/actions/company-actions";
import { getWabaConnectionsAction } from "@/features/waba-connections/actions/waba-connection-actions";
import { CompanyTable } from "@/features/companies/components/company-table";

/**
 * SuperAdmin-only Company management.
 * Protected at four layers: middleware (/superadmin/*), this server guard,
 * the server actions (requiredRole), and the API ([Authorize(Roles="SuperAdmin")]).
 */
export default async function CompaniesPage() {
  const session = await auth();
  if (session?.user?.role !== "SuperAdmin") {
    redirect("/unauthorized");
  }

  const [companies, numbers] = await Promise.all([
    getCompaniesAction({ page: 1, pageSize: 200 }),
    getWabaConnectionsAction({ page: 1, pageSize: 1 }).then(totalOf),
  ]);
  const list = companies.success ? companies.data.items : null;

  return (
    <div className="flex flex-1 flex-col gap-5 p-4 md:p-6">
      <PageHero
        title="Companies"
        description="Every business using Growchat. Add a company here, then connect its WhatsApp number and put it on a plan."
        facts={[
          { label: "Companies", value: list ? list.length : null },
          { label: "Active", value: list ? list.filter((c) => c.isActive).length : null, tone: "green" },
          { label: "WhatsApp numbers", value: numbers },
        ]}
      />

      <CompanyTable />
    </div>
  );
}
