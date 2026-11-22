"use client";

import type { ReactNode } from "react";
import { Controller, type Control, type FieldErrors, type UseFormRegister } from "react-hook-form";
import Link from "next/link";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { SectionCard } from "./section-card";
import { useApprovedTemplates } from "@/features/templates/hooks/use-templates";
import type { SaveBotProfileInput } from "@/features/bot/schema/bot-schema";

/** A Select item cannot have an empty value, so "nothing picked" needs a stand-in. */
const NO_TEMPLATE = "__none__";

/** Customer name, customer number, one-line summary — what the alert template must take. */
const ALERT_VARIABLES = 3;

function variableCount(body: string): number {
  const numbers = [...body.matchAll(/\{\{(\d+)\}\}/g)].map((m) => Number(m[1]));
  return numbers.length ? Math.max(...numbers) : 0;
}

/**
 * What happens when the assistant hands a customer to a person. The chat itself pauses either way; this
 * is how the team finds out. They always get an in-app notification, and — when a template and numbers
 * are set here — a WhatsApp message with the customer's number and a one-line summary.
 */
export function HandoffAlerts({
  control,
  register,
  errors,
  footer,
}: {
  control: Control<SaveBotProfileInput>;
  register: UseFormRegister<SaveBotProfileInput>;
  errors: FieldErrors<SaveBotProfileInput>;
  /** The section's save button. */
  footer?: ReactNode;
}) {
  const hasErrors = Boolean(
    errors.handoffAlertPhones ||
    errors.handoffAlertTemplateId ||
    errors.handoffResumeMinutes ||
    errors.resumeBotAfterIdleHours,
  );
  const { data: templates } = useApprovedTemplates();
  const usable = (templates ?? []).filter(
    (t) => variableCount(t.components.body.text) === ALERT_VARIABLES,
  );

  return (
    <SectionCard
      title="When the assistant hands a chat to your team"
      description={
        <>
          The assistant stops replying on that chat, so someone has to be told. Your team always
          gets an in-app notification. Add numbers and a template to message them on WhatsApp too,
          with the customer&apos;s number and a short summary.
        </>
      }
      contentClassName="space-y-4"
      forceOpen={hasErrors}
    >
      <div className="space-y-1.5">
        <Label>Team WhatsApp numbers</Label>
        <Controller
          control={control}
          name="handoffAlertPhones"
          render={({ field }) => (
            <Textarea
              rows={3}
              placeholder={"94771234567\n94719876543"}
              value={(field.value ?? []).join("\n")}
              // Blank lines are kept while typing so Enter works; the API drops them on save.
              onChange={(e) => field.onChange(e.target.value.split(/[\n,]+/))}
              onBlur={field.onBlur}
            />
          )}
        />
        <p className="text-xs text-muted-foreground">
          One per line, with the country code and no spaces. Up to 5.
        </p>
        {errors.handoffAlertPhones?.message ? (
          <p className="text-xs text-destructive">{errors.handoffAlertPhones.message}</p>
        ) : null}
      </div>

      <div className="space-y-1.5">
        <Label>Alert template</Label>
        <Controller
          control={control}
          name="handoffAlertTemplateId"
          render={({ field }) => (
            <Select
              value={field.value ?? NO_TEMPLATE}
              onValueChange={(v) => field.onChange(v === NO_TEMPLATE ? null : v)}
            >
              <SelectTrigger className="w-full" onBlur={field.onBlur}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NO_TEMPLATE}>No WhatsApp alert — in-app notification only</SelectItem>
                {usable.map((t) => (
                  <SelectItem key={t.id} value={t.id}>
                    {t.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
        {usable.length === 0 ? (
          <p className="text-xs text-amber-600">
            No approved template with three variables yet.{" "}
            <Link href="/templates" className="underline">
              Add the starter templates, then submit &ldquo;staff_handoff_alert&rdquo;
            </Link>{" "}
            — Meta has to approve it before it can be sent.
          </p>
        ) : (
          <p className="text-xs text-muted-foreground">
            {"{{1}}"} is the customer&apos;s name, {"{{2}}"} their number and {"{{3}}"} the summary.
          </p>
        )}
        {errors.handoffAlertTemplateId?.message ? (
          <p className="text-xs text-destructive">{errors.handoffAlertTemplateId.message}</p>
        ) : null}
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-1.5">
          <Label>Give the chat back to the bot after (minutes)</Label>
          <Input type="number" min={0} max={10080} {...register("handoffResumeMinutes")} />
          {errors.handoffResumeMinutes?.message ? (
            <p className="text-xs text-destructive">{errors.handoffResumeMinutes.message}</p>
          ) : (
            <p className="text-xs text-muted-foreground">
              When the bot hands a chat over and nobody picks it up. 0 keeps the bot off until
              someone hands the chat back.
            </p>
          )}
        </div>
        <div className="space-y-1.5">
          <Label>Give the chat back to the bot after (hours)</Label>
          <Input type="number" min={0} max={720} {...register("resumeBotAfterIdleHours")} />
          {errors.resumeBotAfterIdleHours?.message ? (
            <p className="text-xs text-destructive">{errors.resumeBotAfterIdleHours.message}</p>
          ) : (
            <p className="text-xs text-muted-foreground">
              When someone on the team takes a chat over and it then goes quiet this long. 0 means
              never.
            </p>
          )}
        </div>
      </div>
      {footer}
    </SectionCard>
  );
}
