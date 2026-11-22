import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { ContactListTable } from "@/features/contact-lists/components/contact-list-table";

/**
 * CompanyAdmin-only contact-list management — groups of the company's own contacts.
 * Protected at four layers: proxy (auth), this server guard (role), the server actions
 * (requiredRole), and the API ([Authorize(Roles="CompanyAdmin")] + JWT company scoping).
 */
export default async function ContactListsPage() {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      {/* Header card — mirrors the Contacts / Team screen style */}
      <div className="page-header">
        <h1>Contact Lists</h1>
        <p>
          Group contacts into lists for campaigns. Add existing contacts to a list, or import them.
        </p>
      </div>

      <ContactListTable />
    </div>
  );
}
