import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { CampaignTable } from "@/features/campaigns/components/campaign-table";

export default async function CampaignsPage() {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      <div className="rounded-2xl border bg-muted/40 px-8 py-10">
        <h1 className="text-3xl font-bold tracking-tight">Campaigns</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Send WhatsApp template messages to contact lists or individual contacts.
        </p>
      </div>

      <CampaignTable />
    </div>
  );
}
