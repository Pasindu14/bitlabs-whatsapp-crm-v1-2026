import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { CalendarView } from "@/features/calendar/components/calendar-view";
import { PlanFeatureGate } from "@/features/bot/components/plan-feature-gate";

/**
 * CompanyAdmin-only booking calendar. Protected at four layers: proxy (auth), this server guard
 * (role), the server actions (requiredRole), and the API ([RequirePermission(manage_calendar)]
 * + JWT company scoping).
 */
export default async function CalendarPage() {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      <div className="rounded-2xl border bg-muted/40 px-8 py-10">
        <h1 className="text-3xl font-bold tracking-tight">Calendar</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Every appointment your business has booked, in one shared calendar. Slots come from your
          opening hours minus what is already taken — two people can never end up in the same one.
        </p>
      </div>

      <PlanFeatureGate feature="calendar">
        <CalendarView />
      </PlanFeatureGate>
    </div>
  );
}
