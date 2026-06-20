import { auth } from "@/auth";
import { redirect } from "next/navigation";
import { CurrentPlanCard } from "@/features/my-subscription/components/current-plan-card";

export default async function MySubscriptionPage() {
  const session = await auth();
  if (!session?.user) {
    redirect("/login");
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      <div className="rounded-2xl border bg-muted/40 px-8 py-10">
        <h1 className="text-3xl font-bold tracking-tight">My Subscription</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Your current plan, message quota, and renewal date.
        </p>
      </div>

      <div className="max-w-md">
        <CurrentPlanCard />
      </div>
    </div>
  );
}
