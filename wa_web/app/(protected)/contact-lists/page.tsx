import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { PageHero, totalOf } from "@/components/page-hero";
import { getContactsAction } from "@/features/contacts/actions/contact-actions";
import { getContactListsAction } from "@/features/contact-lists/actions/contact-list-actions";
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

  const [lists, contacts] = await Promise.all([
    getContactListsAction({ page: 1, pageSize: 1 }).then(totalOf),
    getContactsAction({ page: 1, pageSize: 1 }).then(totalOf),
  ]);

  return (
    <div className="flex flex-1 flex-col gap-5 p-4 md:p-6">
      <PageHero
        title="Contact lists"
        description="Group contacts so a campaign can reach them all at once. Add people you already have, or import a new list."
        facts={[
          { label: "Lists", value: lists },
          { label: "Contacts to choose from", value: contacts },
        ]}
      />

      <ContactListTable />
    </div>
  );
}
