import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { PageHero } from "@/components/page-hero";
import { LogViewer } from "@/features/log-viewer/components/log-viewer";

export default async function LogsPage() {
  const session = await auth();
  if (session?.user?.role !== "SuperAdmin") {
    redirect("/unauthorized");
  }


  return (
    <div className="flex flex-1 flex-col gap-5 p-4 md:p-6 h-full">
      <PageHero
        title="Live logs"
        description="What the API is doing right now: the latest 100 entries, refreshed every 2 seconds."
      />

      <div className="flex-1 min-h-0">
        <LogViewer />
      </div>
    </div>
  );
}
