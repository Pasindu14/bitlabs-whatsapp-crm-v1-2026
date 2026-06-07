import { redirect } from "next/navigation";
import { auth } from "@/auth";
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

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      {/* Header card — mirrors the User Management screen style */}
      <div className="rounded-2xl border bg-muted/40 px-8 py-10">
        <h1 className="text-3xl font-bold tracking-tight">Company Management</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Create and manage tenant companies on the platform.
        </p>
      </div>

      <CompanyTable />
    </div>
  );
}
