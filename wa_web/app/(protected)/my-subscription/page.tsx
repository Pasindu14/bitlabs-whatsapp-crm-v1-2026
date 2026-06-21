import { auth } from "@/auth";
import { redirect } from "next/navigation";
import { CurrentPlanCard } from "@/features/my-subscription/components/current-plan-card";
import { BillingActionsCard } from "@/features/my-subscription/components/billing-actions";
import { InvoicesList } from "@/features/my-subscription/components/invoices-list";

export default async function MySubscriptionPage() {
  const session = await auth();
  if (!session?.user) {
    redirect("/login");
  }

  const isAdmin = session.user.role === "CompanyAdmin";

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      <div className="rounded-2xl border bg-muted/40 px-8 py-10">
        <h1 className="text-3xl font-bold tracking-tight">My Subscription</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Your current plan, message quota, and renewal date.
        </p>
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <div className="flex flex-col gap-6">
          <CurrentPlanCard />
          {isAdmin && <InvoicesList />}
        </div>
        {isAdmin && (
          <div>
            <BillingActionsCard />
          </div>
        )}
      </div>
    </div>
  );
}
