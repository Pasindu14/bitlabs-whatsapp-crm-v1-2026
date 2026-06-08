import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { ContactTable } from "@/features/contacts/components/contact-table";

/**
 * CompanyAdmin-only contact management — the people the admin's OWN company messages.
 * Protected at four layers: proxy (auth), this server guard (role), the server actions
 * (requiredRole), and the API ([Authorize(Roles="CompanyAdmin")] + JWT company scoping).
 */
export default async function ContactsPage() {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      {/* Header card — mirrors the Team / WABA management screen style */}
      <div className="rounded-2xl border bg-muted/40 px-8 py-10">
        <h1 className="text-3xl font-bold tracking-tight">Contacts</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Add and manage the people your company messages on WhatsApp.
        </p>
      </div>

      <ContactTable />
    </div>
  );
}
