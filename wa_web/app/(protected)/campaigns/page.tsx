import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { PageHero, totalOf } from "@/components/page-hero";
import { getCampaignsAction } from "@/features/campaigns/actions/campaign-actions";
import { CampaignTable } from "@/features/campaigns/components/campaign-table";

export default async function CampaignsPage() {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }

  const count = (status: string) => getCampaignsAction({ page: 1, pageSize: 1, status }).then(totalOf);
  const [running, scheduled, completed, drafts] = await Promise.all([
    count("Running"),
    count("Scheduled"),
    count("Completed"),
    count("Draft"),
  ]);

  return (
    <div className="flex flex-1 flex-col gap-5 p-4 md:p-6">
      <PageHero
        title="Campaigns"
        description="Send an approved WhatsApp template to a contact list or to chosen contacts, now or at a set time."
        facts={[
          { label: "Sending now", value: running, tone: "green" },
          { label: "Scheduled", value: scheduled, tone: "blue" },
          { label: "Completed", value: completed, tone: "plain" },
          { label: "Drafts", value: drafts, tone: "amber" },
        ]}
      />

      <CampaignTable />
    </div>
  );
}
