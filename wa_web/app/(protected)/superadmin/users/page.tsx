import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { PageHero } from "@/components/page-hero";
import { getUsersAction } from "@/features/users/actions/user-actions";
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

  const res = await getUsersAction({ page: 1, pageSize: 200 });
  const list = res.success ? res.data.items : null;

  return (
    <div className="flex flex-1 flex-col gap-5 p-4 md:p-6">
      <PageHero
        title="Users"
        description="Everyone who can sign in to a company: its admins, and the agents who answer chats."
        facts={[
          { label: "Users", value: list ? list.length : null },
          { label: "Active", value: list ? list.filter((u) => u.isActive).length : null, tone: "green" },
        ]}
      />

      <UserTable />
    </div>
  );
}
