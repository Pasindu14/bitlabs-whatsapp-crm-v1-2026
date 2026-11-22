import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { TeamTable } from "@/features/team/components/team-table";

/**
 * CompanyAdmin-only team management — users within the admin's OWN company.
 * Protected at four layers: middleware, this server guard, the server actions
 * (requiredRole), and the API ([Authorize(Roles="CompanyAdmin")] + JWT company scoping).
 */
export default async function TeamPage() {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      {/* Header card — mirrors the SuperAdmin Users screen style */}
      <div className="page-header">
        <h1>Team</h1>
        <p>
          Create and manage Company Admin and Agent users for your company.
        </p>
      </div>

      <TeamTable />
    </div>
  );
}
