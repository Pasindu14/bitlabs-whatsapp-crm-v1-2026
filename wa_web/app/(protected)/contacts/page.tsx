import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { PageHero, totalOf } from "@/components/page-hero";
import { getContactsAction } from "@/features/contacts/actions/contact-actions";
import { getContactListsAction } from "@/features/contact-lists/actions/contact-list-actions";
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

  const [contacts, lists] = await Promise.all([
    getContactsAction({ page: 1, pageSize: 1 }).then(totalOf),
    getContactListsAction({ page: 1, pageSize: 1 }).then(totalOf),
  ]);

  return (
    <div className="flex flex-1 flex-col gap-5 p-4 md:p-6">
      <PageHero
        title="Contacts"
        description="The people your business messages on WhatsApp. Add them one by one, or import a spreadsheet."
        facts={[
          { label: "Contacts", value: contacts },
          { label: "Contact lists", value: lists },
        ]}
      />

      <ContactTable />
    </div>
  );
}
