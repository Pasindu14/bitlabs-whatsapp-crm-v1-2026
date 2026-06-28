import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { CampaignDetail } from "@/features/campaigns/components/campaign-detail";

// Next 16: route params are async and must be awaited (see node_modules/next/dist/docs).
export default async function CampaignDetailPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }
  const { id } = await params;
  return <CampaignDetail campaignId={id} />;
}
