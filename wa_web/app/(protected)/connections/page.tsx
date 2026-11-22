import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { PageHero, totalOf } from "@/components/page-hero";
import { getConnectionsAction } from "@/features/connections/actions/connections-actions";
import { ConnectionsView } from "@/features/connections/components/connections-view";

export default async function ConnectionsPage() {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }

  const res = await getConnectionsAction();
  const all = res.success ? res.data : null;
  const healthy = all ? all.filter((c) => c.status === "Connected").length : null;

  return (
    <div className="flex flex-1 flex-col gap-5 p-4 md:p-6">
      <PageHero
        title="Connections"
        description="The WhatsApp business numbers your company sends from. Each one is checked regularly so you know it can still send."
        facts={[
          { label: "Numbers", value: all ? all.length : null },
          { label: "Working", value: healthy, tone: "green" },
          ...(all && healthy !== null && all.length - healthy > 0
            ? [{ label: "Need attention", value: all.length - healthy, tone: "red" as const }]
            : []),
        ]}
      />
      <ConnectionsView />
    </div>
  );
}
