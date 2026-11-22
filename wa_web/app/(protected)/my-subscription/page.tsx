import { auth } from "@/auth";
import { redirect } from "next/navigation";
import { SubscriptionHero } from "@/features/my-subscription/components/subscription-hero";
import { BillingActionsCard } from "@/features/my-subscription/components/billing-actions";
import { InvoicesList } from "@/features/my-subscription/components/invoices-list";

export default async function MySubscriptionPage() {
  const session = await auth();
  if (!session?.user) {
    redirect("/login");
  }

  const isAdmin = session.user.role === "CompanyAdmin";

  return (
    <div className="flex flex-1 flex-col gap-5 p-4 md:p-6">
      <SubscriptionHero />

      {isAdmin && (
        <div className="grid items-start gap-5 xl:grid-cols-[minmax(0,1.25fr)_minmax(0,1fr)]">
          <BillingActionsCard />
          <InvoicesList />
        </div>
      )}
    </div>
  );
}
