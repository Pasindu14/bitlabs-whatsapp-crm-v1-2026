import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { PageHero, totalOf } from "@/components/page-hero";
import { getTeamAction } from "@/features/team/actions/team-actions";
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

  const members = await getTeamAction({ page: 1, pageSize: 1 }).then(totalOf);

  return (
    <div className="flex flex-1 flex-col gap-5 p-4 md:p-6">
      <PageHero
        title="Team"
        description="Everyone who can sign in for your company. Admins manage everything; agents answer chats with the permissions you give them."
        facts={[{ label: "People", value: members }]}
      />

      <TeamTable />
    </div>
  );
}
