import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { MessageTable } from "@/features/messages/components/message-table";

export default async function MessagesPage() {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      <div className="rounded-2xl border bg-muted/40 px-8 py-10">
        <h1 className="text-3xl font-bold tracking-tight">Messages</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          History of outbound WhatsApp messages sent to your contacts.
        </p>
      </div>

      <MessageTable />
    </div>
  );
}
