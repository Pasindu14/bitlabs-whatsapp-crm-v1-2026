import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { LogViewer } from "@/features/log-viewer/components/log-viewer";

export default async function LogsPage() {
  const session = await auth();
  if (session?.user?.role !== "SuperAdmin") {
    redirect("/unauthorized");
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6 h-full">
      <div className="page-header">
        <h1>Live Logs</h1>
        <p>
          Real-time API log stream — last 100 entries, refreshed every 2 s.
        </p>
      </div>

      <div className="flex-1 min-h-0">
        <LogViewer />
      </div>
    </div>
  );
}
