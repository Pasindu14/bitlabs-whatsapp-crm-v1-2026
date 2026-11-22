"use client";

import { useEffect } from "react";
import { useForm, useWatch, Controller, type FieldErrors, type Resolver } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Bot, Sparkles } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import { Skeleton } from "@/components/ui/skeleton";
import { Spinner } from "@/components/ui/spinner";
import { Card, CardContent } from "@/components/ui/card";
import { useBotProfile, useSaveBotProfile } from "@/features/bot/hooks/use-bot-profile";
import { saveBotProfileSchema, type SaveBotProfileInput } from "@/features/bot/schema/bot-schema";
import { FaqSection } from "./faq-section";
import { SectionCard } from "./section-card";
import { LinkSection } from "./link-section";
import { AiPreviewPanel } from "./ai-preview-panel";
import { FollowUpSteps } from "./follow-up-steps";
import { HandoffAlerts } from "./handoff-alerts";

/**
 * The AI assistant's settings. One PUT covers the whole profile, so this is a single form with a
 * single save — unlike the calendar settings screen, whose sections each have their own endpoint.
 *
 * What is deliberately NOT on this screen: services, prices, opening hours and the timezone. Those
 * live on /calendar/settings — every enabled service there is bookable — so a price can never be right
 * in one place and wrong in the other.
 */
export function AiSettingsView() {
  const { data: profile, isLoading, isError } = useBotProfile();

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-40 w-full rounded-xl" />
        <Skeleton className="h-64 w-full rounded-xl" />
      </div>
    );
  }

  if (isError || !profile) {
    return (
      <Card>
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          Could not load the assistant&apos;s settings. Refresh to try again.
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="space-y-6">
      {/* Keyed on the saved timestamp so a successful save remounts the form with the new values
          rather than needing an effect to sync them — the same trick the calendar settings use. */}
      <ProfileForm key={profile.updatedAt ?? "new"} profile={profile} />
      <FaqSection />
      <LinkSection />
      <AiPreviewPanel canPreview={profile.exists} />
    </div>
  );
}

function ProfileForm({
  profile,
}: {
  profile: NonNullable<ReturnType<typeof useBotProfile>["data"]>;
}) {
  const save = useSaveBotProfile();

  const {
    register,
    control,
    handleSubmit,
    setError,
    setFocus,
    setValue,
    formState: { errors },
  } = useForm<SaveBotProfileInput>({
    resolver: zodResolver(saveBotProfileSchema) as Resolver<SaveBotProfileInput>,
    defaultValues: {
      isEnabled: profile.isEnabled,
      businessName: profile.businessName,
      businessDescription: profile.businessDescription,
      toneNotes: profile.toneNotes,
      location: profile.location,
      contactNote: profile.contactNote,
      bookingInstructions: profile.bookingInstructions,
      preferredLanguage: profile.preferredLanguage,
      currencyLabel: profile.currencyLabel,
      dailyReplyLimit: profile.dailyReplyLimit,
      followUpSteps: (profile.followUpSteps ?? []).map((s) => ({
        ...s,
        templateId: s.templateId ?? null,
        variableOverrides: s.variableOverrides ?? [],
      })),
      resumeBotAfterIdleHours: profile.resumeBotAfterIdleHours,
      handoffAlertPhones: profile.handoffAlertPhones ?? [],
      handoffAlertTemplateId: profile.handoffAlertTemplateId ?? null,
      handoffResumeMinutes: profile.handoffResumeMinutes ?? 0,
      industryRules: profile.industryRules ?? "",
      bookingsEnabled: profile.bookingsEnabled ?? true,
    },
  });

  const bookingsEnabled = useWatch({ control, name: "bookingsEnabled" });

  // Bind API field errors back onto the inputs.
  useEffect(() => {
    if (!save.fieldErrors) return;
    for (const [field, message] of Object.entries(save.fieldErrors)) {
      setError(field as keyof SaveBotProfileInput, { type: "server", message });
    }
  }, [save.fieldErrors, setError]);

  // Without this, a validation error on a field that is scrolled out of view makes Save look dead.
  // The section holding the field opens itself (forceOpen below); the focus waits for that render.
  const onInvalid = (invalid: FieldErrors<SaveBotProfileInput>) => {
    const first = Object.keys(invalid)[0];
    toast.error("Some fields need attention before this can be saved.");
    if (first) setTimeout(() => setFocus(first as keyof SaveBotProfileInput), 0);
  };

  const saveRow = (
    <div className="flex justify-end">
      <Button type="submit" size="sm" disabled={save.isPending}>
        {save.isPending ? <Spinner className="mr-2 size-4" /> : <Bot className="mr-2 size-4" />}
        Save assistant
      </Button>
    </div>
  );

  return (
    <form onSubmit={handleSubmit((data) => save.mutate(data), onInvalid)} className="space-y-6">
      <SectionCard
        title={
          <span className="flex items-center gap-2">
            <Sparkles className="size-4" />
            AI assistant
          </span>
        }
        description={
          <>
            Replies to anything your keyword rules don&apos;t cover, in the customer&apos;s own
            language, and moves them to the next step when they&apos;re ready. Your rules still answer first.
          </>
        }
        actions={
          <Controller
            control={control}
            name="isEnabled"
            render={({ field }) => (
              <Switch
                checked={field.value}
                onCheckedChange={field.onChange}
                aria-label="Enable the AI assistant"
              />
            )}
          />
        }
        actionsWhenClosed
        defaultOpen
        forceOpen={Boolean(errors.dailyReplyLimit)}
      >
        <Field
          label="Daily reply limit"
          error={errors.dailyReplyLimit?.message}
          hint="A safety net on cost. Past this many replies in a day, the assistant stops until tomorrow."
        >
          <Input type="number" min={0} className="sm:max-w-xs" {...register("dailyReplyLimit")} />
        </Field>
        {saveRow}
      </SectionCard>

      <SectionCard
        title="About the business"
        description={
          <>
            Everything here is quoted to customers word for word, so write it the way you&apos;d
            want it said. The assistant will never make up anything you haven&apos;t told it.
          </>
        }
        forceOpen={Boolean(
          errors.businessName ||
          errors.businessDescription ||
          errors.location ||
          errors.contactNote ||
          errors.currencyLabel ||
          errors.preferredLanguage,
        )}
      >
        <Field label="Business name" error={errors.businessName?.message} required>
          <Input {...register("businessName")} placeholder="Colombo Dental" />
        </Field>

        <Field
          label="What you do"
          error={errors.businessDescription?.message}
          hint="A short paragraph. What you offer, who it's for, anything that makes you the right choice."
          required
        >
          <Textarea
            {...register("businessDescription")}
            rows={5}
            placeholder="A family dental clinic in Nugegoda. We've been open since 2011 and see everything from routine cleanings to emergency work…"
          />
        </Field>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Where you are" error={errors.location?.message}>
            <Input {...register("location")} placeholder="12 High Level Road, Nugegoda" />
          </Field>
          <Field label="Contact details" error={errors.contactNote?.message}>
            <Input {...register("contactNote")} placeholder="011 555 0000" />
          </Field>
          <Field
            label="Currency label"
            error={errors.currencyLabel?.message}
            hint="How prices are written — Rs., LKR, $."
          >
            <Input {...register("currencyLabel")} placeholder="Rs." />
          </Field>
          <Field
            label="Reply language"
            error={errors.preferredLanguage?.message}
            hint="It always answers in this language. Leave blank to mirror the customer."
          >
            <Input {...register("preferredLanguage")} placeholder="English" />
          </Field>
        </div>
        {saveRow}
      </SectionCard>

      <SectionCard
        title="Rules"
        description={
          <>
            It already writes short, warm WhatsApp messages. Your answers below are its script —
            this is only for anything else specific to you.
          </>
        }
        forceOpen={Boolean(errors.toneNotes || errors.bookingInstructions || errors.industryRules)}
      >
        <div className="flex items-start justify-between gap-4 rounded-lg border p-3">
          <div className="space-y-0.5">
            <Label htmlFor="bookingsEnabled">Takes bookings</Label>
            <p className="text-xs text-muted-foreground">
              On: it checks free times and books, moves and cancels appointments. Off: for shops and
              order lines. It answers questions and points customers to your next step instead.
            </p>
          </div>
          <Controller
            control={control}
            name="bookingsEnabled"
            render={({ field }) => (
              <Switch id="bookingsEnabled" checked={field.value} onCheckedChange={field.onChange} />
            )}
          />
        </div>

        <Field
          label="Tone"
          error={errors.toneNotes?.message}
          hint="For example: keep it casual, use the customer's first name, never push a sale twice."
        >
          <Textarea {...register("toneNotes")} rows={3} placeholder="Warm and casual…" />
        </Field>

        <Field
          label={bookingsEnabled ? "Booking rules" : "Next step"}
          error={errors.bookingInstructions?.message}
          hint={
            bookingsEnabled
              ? "Anything it should always do before booking someone in."
              : "Where it sends a customer who is ready: visit the shop, call, order on the website."
          }
        >
          <Textarea
            {...register("bookingInstructions")}
            rows={4}
            placeholder={
              bookingsEnabled
                ? "Always confirm which service they want before offering times."
                : "Ask them to visit the shop on Peradeniya Road, or call 081 222 3344 to order."
            }
          />
        </Field>

        <Field
          label="Rules for your type of business"
          error={errors.industryRules?.message}
          hint="Rules only your trade needs. Followed as strictly as the built-in ones. Leave blank if none."
        >
          <Textarea
            {...register("industryRules")}
            rows={5}
            placeholder="- Never promise a delivery date. The team confirms it."
          />
          <div className="flex flex-wrap gap-2 pt-1">
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() =>
                setValue("industryRules", HEALTHCARE_RULES, { shouldDirty: true, shouldValidate: true })
              }
            >
              Use healthcare rules
            </Button>
          </div>
        </Field>
        {saveRow}
      </SectionCard>

      <HandoffAlerts control={control} register={register} errors={errors} footer={saveRow} />

      <FollowUpSteps control={control} register={register} errors={errors} footer={saveRow} />
    </form>
  );
}

/**
 * Mirrors wa_api IndustryRulePresets.Healthcare, word for word: the medical rules every clinic had
 * built in before plan 015 made the prompt generic.
 */
const HEALTHCARE_RULES = [
  "- Do NOT practise medicine: never say what is causing something, how serious it is, whether a " +
    "treatment will work for them, or anything about their medication. General facts about a treatment " +
    "(what it involves, pain, time, downtime, cost) are yours to give whenever the details cover them.",
  "- If they tell you something is wrong, ask ONE simple question: what is bothering them, or how long " +
    "it has been going on.",
  "- A medical question is a reason to hand off: tell them the team will get back to them and use " +
    "handoff_to_human.",
].join("\n");

function Field({
  label,
  hint,
  error,
  required,
  children,
}: {
  label: string;
  hint?: string;
  error?: string;
  required?: boolean;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1.5">
      <Label>
        {label}
        {required ? <span className="ml-0.5 text-destructive">*</span> : null}
      </Label>
      {children}
      {hint && !error ? <p className="text-xs text-muted-foreground">{hint}</p> : null}
      {error ? <p className="text-xs text-destructive">{error}</p> : null}
    </div>
  );
}
