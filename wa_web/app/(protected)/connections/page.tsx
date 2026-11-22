import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { ConnectionsView } from "@/features/connections/components/connections-view";

export default async function ConnectionsPage() {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      <div className="page-header">
        <h1>Connections</h1>
        <p>
          Your WhatsApp Business phone number connections.
        </p>
      </div>
      <ConnectionsView />
    </div>
  );
}
