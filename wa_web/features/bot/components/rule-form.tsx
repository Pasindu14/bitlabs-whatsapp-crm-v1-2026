"use client";

import { useEffect } from "react";
import { useForm, useWatch, Controller, type Resolver } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Checkbox } from "@/components/ui/checkbox";
import { Spinner } from "@/components/ui/spinner";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  AUTO_REPLY_TRIGGERS,
  TERMLESS_TRIGGERS,
  TRIGGER_HINTS,
  TRIGGER_LABELS,
  type AutoReplyTrigger,
} from "@/features/bot/types";
import {
  createAutoReplyRuleSchema,
  updateAutoReplyRuleSchema,
  type CreateAutoReplyRuleInput,
} from "@/features/bot/schema/bot-schema";

interface RuleFormProps {
  mode: "create" | "edit";
  defaultValues?: Partial<CreateAutoReplyRuleInput>;
  onSubmit: (data: CreateAutoReplyRuleInput) => void;
  isLoading: boolean;
  fieldErrors?: Record<string, string> | null;
}

export function RuleForm({
  mode,
  defaultValues,
  onSubmit,
  isLoading,
  fieldErrors,
}: RuleFormProps) {
  const {
    register,
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<CreateAutoReplyRuleInput>({
    resolver: zodResolver(
      mode === "create" ? createAutoReplyRuleSchema : updateAutoReplyRuleSchema
    ) as Resolver<CreateAutoReplyRuleInput>,
    defaultValues: {
      name: "",
      trigger: "Keyword",
      terms: [],
      replyBody: "",
      priority: 0,
      isEnabled: true,
      ...defaultValues,
    },
  });

  // Bind API field errors (e.g. duplicate name) back onto the inputs.
  useEffect(() => {
    if (fieldErrors) {
      for (const [field, message] of Object.entries(fieldErrors)) {
        setError(field as keyof CreateAutoReplyRuleInput, { message });
      }
    }
  }, [fieldErrors, setError]);

  // useWatch (not watch()) so the React Compiler can still memoize this component.
  const trigger = useWatch({ control, name: "trigger" });
  // Welcome and Fallback match on position, so the terms input would be a lie — the
  // server drops their terms anyway (AutoReplyRuleService.NormalizeTerms).
  const needsTerms = !TERMLESS_TRIGGERS.includes(trigger);

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="name">Rule Name</Label>
        <Input id="name" placeholder="Greeting" {...register("name")} />
        {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
      </div>

      <div className="space-y-2">
        <Label htmlFor="trigger">Trigger</Label>
        <Controller
          control={control}
          name="trigger"
          render={({ field }) => (
            <Select
              value={field.value}
              onValueChange={(v) => field.onChange(v as AutoReplyTrigger)}
            >
              <SelectTrigger id="trigger" className="w-full">
                <SelectValue placeholder="Choose a trigger" />
              </SelectTrigger>
              <SelectContent>
                {AUTO_REPLY_TRIGGERS.map((t) => (
                  <SelectItem key={t} value={t}>
                    {TRIGGER_LABELS[t]}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
        <p className="text-xs text-muted-foreground">{TRIGGER_HINTS[trigger]}</p>
        {errors.trigger && (
          <p className="text-xs text-destructive">{errors.trigger.message}</p>
        )}
      </div>

      {needsTerms && (
        <div className="space-y-2">
          <Label htmlFor="terms">Trigger Terms</Label>
          <Controller
            control={control}
            name="terms"
            render={({ field }) => (
              <Input
                id="terms"
                placeholder="hi, hello, hey"
                defaultValue={(field.value ?? []).join(", ")}
                onChange={(e) =>
                  field.onChange(
                    e.target.value
                      .split(",")
                      .map((t) => t.trim())
                      .filter(Boolean)
                  )
                }
              />
            )}
          />
          <p className="text-xs text-muted-foreground">
            Comma-separated. Matching is case-insensitive — any one term is enough.
          </p>
          {errors.terms && (
            <p className="text-xs text-destructive">{errors.terms.message as string}</p>
          )}
        </div>
      )}

      <div className="space-y-2">
        <Label htmlFor="replyBody">Reply Message</Label>
        <Textarea
          id="replyBody"
          rows={4}
          placeholder="Hi! Thanks for reaching out — how can we help?"
          {...register("replyBody")}
        />
        {errors.replyBody && (
          <p className="text-xs text-destructive">{errors.replyBody.message}</p>
        )}
      </div>

      <div className="space-y-2">
        <Label htmlFor="priority">Priority</Label>
        <Input id="priority" type="number" min={0} step={1} {...register("priority")} />
        <p className="text-xs text-muted-foreground">
          Lower runs first, and the first match wins. Give a catch-all rule a high number so
          the specific rules get a chance.
        </p>
        {errors.priority && (
          <p className="text-xs text-destructive">{errors.priority.message}</p>
        )}
      </div>

      <Controller
        control={control}
        name="isEnabled"
        render={({ field }) => (
          <label className="flex items-start gap-3 rounded-md border p-3 cursor-pointer">
            <Checkbox
              className="mt-0.5"
              checked={field.value ?? false}
              onCheckedChange={(c) => field.onChange(c === true)}
            />
            <div className="space-y-1">
              <div className="text-sm font-medium">Enabled</div>
              <p className="text-xs text-muted-foreground">
                Disabled rules are skipped by the bot but kept for later.
              </p>
            </div>
          </label>
        )}
      />

      <Button type="submit" className="w-full" disabled={isLoading}>
        {isLoading ? (
          <>
            <Spinner className="mr-2 size-4" />
            {mode === "create" ? "Creating…" : "Updating…"}
          </>
        ) : mode === "create" ? (
          "Create Rule"
        ) : (
          "Update Rule"
        )}
      </Button>
    </form>
  );
}
