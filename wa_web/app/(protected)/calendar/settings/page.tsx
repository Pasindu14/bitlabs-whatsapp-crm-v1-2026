import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { CalendarSettingsView } from "@/features/calendar/components/calendar-settings-view";
import { PlanFeatureGate } from "@/features/bot/components/plan-feature-gate";

/**
 * CompanyAdmin-only calendar configuration — timezone, bookable services, opening hours and closed
 * periods. Same four protection layers as the calendar itself.
 */
export default async function CalendarSettingsPage() {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      <div className="rounded-2xl border bg-muted/40 px-8 py-10">
        <h1 className="text-3xl font-bold tracking-tight">Calendar settings</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          What can be booked, when you are open, and when you are closed. Everything here feeds the
          slots customers are offered.
        </p>
      </div>

      <PlanFeatureGate feature="calendar">
        <CalendarSettingsView />
      </PlanFeatureGate>
    </div>
  );
}
