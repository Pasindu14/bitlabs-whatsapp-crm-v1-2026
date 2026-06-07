"use client";

import { useEffect } from "react";
import { useForm, Controller, type Resolver } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Spinner } from "@/components/ui/spinner";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  changePlanSchema,
  type ChangePlanInput,
} from "@/features/subscriptions/schema/subscription-schema";
import { usePlanOptions } from "@/features/subscriptions/hooks/use-subscriptions";

interface ChangePlanFormProps {
  onSubmit: (data: ChangePlanInput) => void;
  isLoading: boolean;
  fieldErrors?: Record<string, string> | null;
}

export function ChangePlanForm({ onSubmit, isLoading, fieldErrors }: ChangePlanFormProps) {
  const { data: plans, isLoading: plansLoading } = usePlanOptions();

  const {
    register,
    control,
    handleSubmit,
    setError,
    watch,
    formState: { errors },
  } = useForm<ChangePlanInput>({
    resolver: zodResolver(changePlanSchema) as Resolver<ChangePlanInput>,
    defaultValues: { planId: "", resetPeriod: false, periodDays: 30 },
  });

  const resetPeriod = watch("resetPeriod");

  useEffect(() => {
    if (fieldErrors) {
      for (const [field, message] of Object.entries(fieldErrors)) {
        setError(field as keyof ChangePlanInput, { message });
      }
    }
  }, [fieldErrors, setError]);

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="planId">New Plan</Label>
        <Controller
          control={control}
          name="planId"
          render={({ field }) => (
            <Select
              value={field.value || undefined}
              onValueChange={field.onChange}
              disabled={plansLoading}
            >
              <SelectTrigger id="planId" className="w-full">
                <SelectValue placeholder={plansLoading ? "Loading plans…" : "Select a plan"} />
              </SelectTrigger>
              <SelectContent>
                {(plans ?? []).map((p) => (
                  <SelectItem key={p.id} value={p.id}>
                    {p.name} ({p.quota.toLocaleString()} msgs)
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
        {errors.planId && <p className="text-xs text-destructive">{errors.planId.message}</p>}
      </div>

      <div className="flex items-center justify-between rounded-md border p-3">
        <div className="space-y-0.5">
          <Label htmlFor="resetPeriod">Reset period &amp; usage</Label>
          <p className="text-xs text-muted-foreground">
            Start a fresh period and zero the message count.
          </p>
        </div>
        <Controller
          control={control}
          name="resetPeriod"
          render={({ field }) => (
            <Switch
              id="resetPeriod"
              checked={field.value ?? false}
              onCheckedChange={field.onChange}
            />
          )}
        />
      </div>

      {resetPeriod && (
        <div className="space-y-2">
          <Label htmlFor="periodDays">New period (days)</Label>
          <Input
            id="periodDays"
            type="number"
            min={1}
            max={366}
            step={1}
            placeholder="30"
            {...register("periodDays")}
          />
          {errors.periodDays && (
            <p className="text-xs text-destructive">{errors.periodDays.message}</p>
          )}
        </div>
      )}

      <Button type="submit" className="w-full" disabled={isLoading}>
        {isLoading ? (
          <>
            <Spinner className="mr-2 size-4" />
            Saving…
          </>
        ) : (
          "Change Plan"
        )}
      </Button>
    </form>
  );
}
