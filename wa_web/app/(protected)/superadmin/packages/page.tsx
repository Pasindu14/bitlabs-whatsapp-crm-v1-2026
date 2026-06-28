import { redirect } from "next/navigation";
import { auth } from "@/auth";
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

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      {/* Header card — mirrors the Plans screen style */}
      <div className="rounded-2xl border bg-muted/40 px-8 py-10">
        <h1 className="text-3xl font-bold tracking-tight">Packages</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Define add-on bundles of extra message credits. SuperAdmin can add these to a
          company&apos;s subscription to top up its monthly quota.
        </p>
      </div>

      <PackageTable />
    </div>
  );
}
