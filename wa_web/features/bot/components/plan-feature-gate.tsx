"use client";

import type { ReactNode } from "react";
import { Lock } from "lucide-react";
import { Spinner } from "@/components/ui/spinner";
import { useBotAccess } from "@/features/bot/hooks/use-bot-access";

/**
 * Shows its children only when the company's plan includes the feature. Without this the screens would
 * load and then fail every request with PLAN_FEATURE_REQUIRED — the API is the real gate, this is the
 * explanation.
 */
export function PlanFeatureGate({
  feature,
  children,
}: {
  feature: "chatbot" | "calendar";
  children: ReactNode;
}) {
  const access = useBotAccess();

  if (access.isLoading) {
    return (
      <div className="flex justify-center py-16">
        <Spinner />
      </div>
    );
  }

  if (!access[feature]) {
    const name = feature === "chatbot" ? "The chatbot" : "The calendar";
    return (
      <div className="flex flex-col items-center gap-3 rounded-2xl border border-dashed px-8 py-16 text-center">
        <Lock className="size-8 text-muted-foreground" />
        <h2 className="text-lg font-semibold">{name} is not part of your plan</h2>
        <p className="max-w-md text-sm text-muted-foreground">
          Contact your platform administrator to add it to your subscription.
        </p>
      </div>
    );
  }

  return (
    <>
      {feature === "chatbot" && !access.platformEnabled ? (
        <div className="rounded-xl border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-200">
          The chatbot is switched off for the whole platform right now. You can set it up, but it will not
          reply to customers until your platform administrator switches it on.
        </div>
      ) : null}
      {children}
    </>
  );
}
