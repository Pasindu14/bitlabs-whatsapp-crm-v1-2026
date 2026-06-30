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
      <div className="rounded-2xl border bg-muted/40 px-8 py-10">
        <h1 className="text-3xl font-bold tracking-tight">Live Logs</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Real-time API log stream — last 1 000 entries, refreshed every 2 s.
        </p>
      </div>

      <div className="flex-1 min-h-0">
        <LogViewer />
      </div>
    </div>
  );
}
