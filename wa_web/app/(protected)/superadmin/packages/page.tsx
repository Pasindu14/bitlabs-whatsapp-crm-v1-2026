import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { PageHero, totalOf } from "@/components/page-hero";
import { getPackagesAction } from "@/features/packages/actions/package-actions";
import { PackageTable } from "@/features/packages/components/package-table";

/**
 * SuperAdmin-only message-credit package catalog.
 * Protected at four layers: middleware (/superadmin/*), this server guard,
 * the server actions (requiredRole), and the API ([Authorize(Roles="SuperAdmin")]).
 */
export default async function PackagesPage() {
  const session = await auth();
  if (session?.user?.role !== "SuperAdmin") {
    redirect("/unauthorized");
  }

  const packages = await getPackagesAction({ page: 1, pageSize: 1 }).then(totalOf);

  return (
    <div className="flex flex-1 flex-col gap-5 p-4 md:p-6">
      <PageHero
        title="Packages"
        description="Extra message bundles. Add one to a company's subscription to top up its allowance without changing its plan."
        facts={[{ label: "Packages", value: packages }]}
      />

      <PackageTable />
    </div>
  );
}
