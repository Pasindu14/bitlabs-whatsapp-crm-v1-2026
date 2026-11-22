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
      <div className="page-header">
        <h1>Messages</h1>
        <p>
          History of outbound WhatsApp messages sent to your contacts.
        </p>
      </div>

      <MessageTable />
    </div>
  );
}
