import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { NotificationsView } from "@/features/notifications/components/notifications-view";

export default async function NotificationsPage() {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      <div className="page-header">
        <h1>Notifications</h1>
        <p>
          Campaign alerts, quota warnings, and template approvals.
        </p>
      </div>
      <NotificationsView />
    </div>
  );
}
