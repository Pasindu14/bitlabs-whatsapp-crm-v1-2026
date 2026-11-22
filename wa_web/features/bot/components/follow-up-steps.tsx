"use client";

import type { ReactNode } from "react";
import {
  Controller,
  useFieldArray,
  useWatch,
  type Control,
  type FieldErrors,
  type UseFormRegister,
} from "react-hook-form";
import Link from "next/link";
import { Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { NativeSelect } from "@/components/ui/native-select";
import { Textarea } from "@/components/ui/textarea";
import { SectionCard } from "./section-card";
import { useApprovedTemplates } from "@/features/templates/hooks/use-templates";
import type { SaveBotProfileInput } from "@/features/bot/schema/bot-schema";

/** Meta only accepts free-text messages this long after the customer's last message. */
const WINDOW_HOURS = 24;
const MAX_STEPS = 10;

/**
 * The follow-up sequence a quiet customer gets. Delays are entered in hours and stored in minutes,
 * and every delay counts from the assistant's last reply — so "Day 4" is 96 whatever the steps before
 * it say.
 *
 * A step holds BOTH a script and a template, and the clock decides which is used: inside Meta's
 * 24-hour window the assistant rewrites the script, outside it only an approved template can be sent.
 * So a step past 24 hours without a template is saved, but skipped when it comes due.
 */
export function FollowUpSteps({
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
  const hasErrors = Boolean(errors.followUpSteps);
  const { fields, append, remove } = useFieldArray({
    control,
    name: "followUpSteps",
  });
  const { data: templates } = useApprovedTemplates();
  const steps = useWatch({ control, name: "followUpSteps" });

  return (
    <SectionCard
      title="Follow-up messages"
      description={
        <>
          If a customer goes quiet, the assistant sends these in order and stops as soon as they
          reply or book. Within 24 hours it rewrites your wording in its own words; after that
          WhatsApp only allows an approved template, so pick one for the later steps. With no steps,
          it sends one nudge after six hours.
        </>
      }
      contentClassName="space-y-4"
      forceOpen={hasErrors}
    >
      {fields.map((field, index) => (
        <div key={field.id} className="space-y-2 rounded-lg border p-3">
          <div className="flex items-end gap-3">
            <div className="w-40 space-y-1.5">
              <label className="text-sm font-medium">Send after (hours)</label>
              <Controller
                control={control}
                name={`followUpSteps.${index}.delayMinutes`}
                render={({ field: f }) => (
                  <Input
                    type="number"
                    min={1}
                    step="any"
                    value={f.value == null ? "" : +(f.value / 60).toFixed(2)}
                    onChange={(e) =>
                      f.onChange(
                        e.target.value === "" ? 0 : Math.round(Number(e.target.value) * 60),
                      )
                    }
                    onBlur={f.onBlur}
                  />
                )}
              />
            </div>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="ml-auto"
              onClick={() => remove(index)}
              aria-label="Remove this follow-up"
            >
              <Trash2 className="size-4" />
            </Button>
          </div>
          <Textarea
            {...register(`followUpSteps.${index}.message`)}
            rows={2}
            placeholder="Anything else I can help with, or shall I set it up for you?"
          />

          {/* Past 24 hours the script cannot be sent, so the template is the whole message. */}
          {(steps?.[index]?.delayMinutes ?? 0) > WINDOW_HOURS * 60 && (
            <div className="space-y-1.5 rounded-md bg-muted/50 p-2.5">
              <label className="text-xs font-medium">
                Template to send (required past 24 hours)
              </label>
              <Controller
                control={control}
                name={`followUpSteps.${index}.templateId`}
                render={({ field: f }) => (
                  <NativeSelect
                    value={f.value ?? ""}
                    onChange={(e) => f.onChange(e.target.value || null)}
                    onBlur={f.onBlur}
                  >
                    <option value="">No template — this step will be skipped</option>
                    {(templates ?? []).map((t) => (
                      <option key={t.id} value={t.id}>
                        {t.name}
                      </option>
                    ))}
                  </NativeSelect>
                )}
              />
              {(templates ?? []).length === 0 ? (
                <p className="text-xs text-amber-600">
                  No approved templates yet.{" "}
                  <Link href="/templates" className="underline">
                    Create and submit one
                  </Link>{" "}
                  — Meta has to approve the wording before it can be sent.
                </p>
              ) : (
                <p className="text-xs text-muted-foreground">
                  The customer&apos;s first name fills {"{{1}}"}. Anything else is taken from your
                  business details, and the step is skipped rather than guessed at.
                </p>
              )}
            </div>
          )}
          {(errors.followUpSteps?.[index]?.delayMinutes?.message ||
            errors.followUpSteps?.[index]?.message?.message) && (
            <p className="text-xs text-destructive">
              {errors.followUpSteps?.[index]?.delayMinutes?.message ??
                errors.followUpSteps?.[index]?.message?.message}
            </p>
          )}
        </div>
      ))}

      {errors.followUpSteps?.message && (
        <p className="text-xs text-destructive">{errors.followUpSteps.message}</p>
      )}

      <Button
        type="button"
        variant="outline"
        size="sm"
        disabled={fields.length >= MAX_STEPS}
        onClick={() => {
          const last = fields.length > 0 ? fields[fields.length - 1].delayMinutes : 0;
          append({
            delayMinutes: last > 0 ? last + 24 * 60 : 6 * 60,
            message: "",
            templateId: null,
            variableOverrides: [],
          });
        }}
      >
        <Plus className="mr-2 size-4" />
        Add follow-up
      </Button>
      {footer}
    </SectionCard>
  );
}
