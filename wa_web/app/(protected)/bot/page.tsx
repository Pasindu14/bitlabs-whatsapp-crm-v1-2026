import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { BotWorkspace } from "@/features/bot/components/bot-workspace";
import { PlanFeatureGate } from "@/features/bot/components/plan-feature-gate";
import { ChatbotHero } from "@/features/bot/components/chatbot-hero";

/**
 * CompanyAdmin-only chatbot management. Protected at four layers: proxy (auth), this server
 * guard (role), the server actions (requiredRole), and the API ([RequirePermission(manage_bot)]
 * + JWT company scoping).
 */
export default async function BotPage() {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }

  return (
    <div className="flex flex-1 flex-col gap-5 p-4 md:p-6">
      <ChatbotHero />

      <PlanFeatureGate feature="chatbot">
        <BotWorkspace />
      </PlanFeatureGate>
    </div>
  );
}
