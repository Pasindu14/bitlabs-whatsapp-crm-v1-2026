"use client";

import { Sparkles } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useBotAccess } from "@/features/bot/hooks/use-bot-access";
import { useCreateStarterPack } from "@/features/templates/hooks/use-templates";

/**
 * Adds the chatbot's ready-made follow-up, appointment and staff-alert templates as drafts. Only shown
 * to companies whose plan includes the chatbot — nobody else has a use for them.
 */
export function StarterPackButton() {
  const access = useBotAccess();
  const createStarterPack = useCreateStarterPack();

  if (!access.chatbot) return null;

  return (
    <Button
      variant="outline"
      size="sm"
      disabled={createStarterPack.isPending}
      onClick={() => createStarterPack.mutate()}
    >
      <Sparkles className="size-4" />
      {createStarterPack.isPending ? "Adding…" : "Add chatbot starter templates"}
    </Button>
  );
}
