import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { env } from "@/lib/env";
import { Inbox } from "@/features/conversations/components/inbox";

export default async function InboxPage() {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }

  // API_URL is a server-only env var; pass it to the client inbox so SignalR can reach the hub.
  return <Inbox apiUrl={env.API_URL} />;
}
