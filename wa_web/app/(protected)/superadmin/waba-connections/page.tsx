import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { PageHero } from "@/components/page-hero";
import { getWabaConnectionsAction } from "@/features/waba-connections/actions/waba-connection-actions";
import { WabaConnectionTable } from "@/features/waba-connections/components/waba-connection-table";

/**
 * SuperAdmin-only WABA connection management.
 * Protected at four layers: middleware (/superadmin/*), this server guard,
 * the server actions (requiredRole), and the API ([Authorize(Roles="SuperAdmin")]).
 */
export default async function WabaConnectionsPage() {
  const session = await auth();
  if (session?.user?.role !== "SuperAdmin") {
    redirect("/unauthorized");
  }

  const res = await getWabaConnectionsAction({ page: 1, pageSize: 200 });
  const list = res.success ? res.data.items : null;
  const working = list ? list.filter((c) => c.status === "Connected").length : null;
  const broken = list && working !== null ? list.length - working : null;

  return (
    <div className="flex flex-1 flex-col gap-5 p-4 md:p-6">
      <PageHero
        title="WhatsApp numbers"
        description="The business numbers companies send from. Each needs a valid access token from Meta; a broken one stops that company sending."
        facts={[
          { label: "Numbers", value: list ? list.length : null },
          { label: "Working", value: working, tone: "green" },
          ...(broken ? [{ label: "Need a new token", value: broken, tone: "red" as const }] : []),
        ]}
      />

      <WabaConnectionTable />
    </div>
  );
}
