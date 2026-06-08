import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { TemplateTable } from "@/features/templates/components/template-table";

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
      <div className="rounded-2xl border bg-muted/40 px-8 py-10">
        <h1 className="text-3xl font-bold tracking-tight">Message Templates</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Build WhatsApp marketing templates, submit them to Meta, and track approval status.
        </p>
      </div>

      <TemplateTable />
    </div>
  );
}
