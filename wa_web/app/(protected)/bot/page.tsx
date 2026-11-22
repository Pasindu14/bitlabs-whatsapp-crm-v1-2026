import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { BotWorkspace } from "@/features/bot/components/bot-workspace";
import { PlanFeatureGate } from "@/features/bot/components/plan-feature-gate";

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
    <div className="flex flex-1 flex-col gap-6 p-6">
      <div className="page-header">
        <h1>Chatbot</h1>
        <p>
          Reply to incoming WhatsApp messages automatically. Your rules answer first, word for word;
          the AI assistant handles everything else and books customers straight into your calendar.
          Neither ever replies to someone who has opted out, or talks over an agent who has stepped in.
        </p>
      </div>

      <PlanFeatureGate feature="chatbot">
        <BotWorkspace />
      </PlanFeatureGate>
    </div>
  );
}
