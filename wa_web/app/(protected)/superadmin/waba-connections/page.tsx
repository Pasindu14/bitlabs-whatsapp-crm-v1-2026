import { redirect } from "next/navigation";
import { auth } from "@/auth";
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

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      {/* Header card — mirrors the Company Management screen style */}
      <div className="rounded-2xl border bg-muted/40 px-8 py-10">
        <h1 className="text-3xl font-bold tracking-tight">WABA Connections</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Connect and manage WhatsApp Business phone numbers for tenant companies.
        </p>
      </div>

      <WabaConnectionTable />
    </div>
  );
}
