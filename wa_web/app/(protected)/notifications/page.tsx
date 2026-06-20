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
      <div className="rounded-2xl border bg-muted/40 px-8 py-10">
        <h1 className="text-3xl font-bold tracking-tight">Notifications</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Campaign alerts, quota warnings, and template approvals.
        </p>
      </div>
      <NotificationsView />
    </div>
  );
}
