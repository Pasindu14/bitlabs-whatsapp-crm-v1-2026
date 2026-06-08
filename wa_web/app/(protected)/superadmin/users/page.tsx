import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { UserTable } from "@/features/users/components/user-table";

/**
 * SuperAdmin-only tenant-user management.
 * Protected at four layers: middleware (/superadmin/*), this server guard,
 * the server actions (requiredRole), and the API ([Authorize(Roles="SuperAdmin")]).
 */
export default async function UsersPage() {
  const session = await auth();
  if (session?.user?.role !== "SuperAdmin") {
    redirect("/unauthorized");
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      {/* Header card — mirrors the Company Management screen style */}
      <div className="rounded-2xl border bg-muted/40 px-8 py-10">
        <h1 className="text-3xl font-bold tracking-tight">Users</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Create and manage Company Admin and Agent users for tenant companies.
        </p>
      </div>

      <UserTable />
    </div>
  );
}
