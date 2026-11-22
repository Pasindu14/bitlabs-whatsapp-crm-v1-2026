import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { TemplateTable } from "@/features/templates/components/template-table";
import { StarterPackButton } from "@/features/templates/components/starter-pack-button";

/**
 * CompanyAdmin-only template management. Protected at four layers: proxy (auth), this server
 * guard (role), the server actions (requiredRole), and the API ([Authorize(Roles="CompanyAdmin")]
 * + JWT company scoping). Plan 004.
 */
export default async function TemplatesPage() {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      <div className="page-header">
        <h1>Message Templates</h1>
        <p>
          Build WhatsApp marketing templates, submit them to Meta, and track approval status.
        </p>
        <div className="mt-4">
          <StarterPackButton />
        </div>
      </div>

      <TemplateTable />
    </div>
  );
}
