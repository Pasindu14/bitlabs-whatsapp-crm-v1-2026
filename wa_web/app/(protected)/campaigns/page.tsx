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
      <div className="page-header">
        <h1>Campaigns</h1>
        <p>
          Send WhatsApp template messages to contact lists or individual contacts.
        </p>
      </div>

      <CampaignTable />
    </div>
  );
}
