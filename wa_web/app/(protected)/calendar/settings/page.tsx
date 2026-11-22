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
      <div className="page-header">
        <h1>Calendar settings</h1>
        <p>
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
