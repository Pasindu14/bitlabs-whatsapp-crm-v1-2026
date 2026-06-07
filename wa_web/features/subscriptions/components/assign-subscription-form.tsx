"use client";

import { useEffect } from "react";
import { useForm, Controller, type Resolver } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  assignSubscriptionSchema,
  type AssignSubscriptionInput,
} from "@/features/subscriptions/schema/subscription-schema";
import {
  useCompanyOptions,
  usePlanOptions,
} from "@/features/subscriptions/hooks/use-subscriptions";

interface AssignSubscriptionFormProps {
  onSubmit: (data: AssignSubscriptionInput) => void;
  isLoading: boolean;
  fieldErrors?: Record<string, string> | null;
}

export function AssignSubscriptionForm({
  onSubmit,
  isLoading,
  fieldErrors,
}: AssignSubscriptionFormProps) {
  const { data: companies, isLoading: companiesLoading } = useCompanyOptions();
  const { data: plans, isLoading: plansLoading } = usePlanOptions();

  const {
    register,
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<AssignSubscriptionInput>({
    resolver: zodResolver(assignSubscriptionSchema) as Resolver<AssignSubscriptionInput>,
    defaultValues: { companyId: "", planId: "", periodDays: 30 },
  });

  useEffect(() => {
    if (fieldErrors) {
      for (const [field, message] of Object.entries(fieldErrors)) {
        setError(field as keyof AssignSubscriptionInput, { message });
      }
    }
  }, [fieldErrors, setError]);

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="companyId">Company</Label>
        <Controller
          control={control}
          name="companyId"
          render={({ field }) => (
            <Select
              value={field.value || undefined}
              onValueChange={field.onChange}
              disabled={companiesLoading}
            >
              <SelectTrigger id="companyId" className="w-full">
                <SelectValue
                  placeholder={companiesLoading ? "Loading companies…" : "Select a company"}
                />
              </SelectTrigger>
              <SelectContent>
                {(companies ?? []).map((c) => (
                  <SelectItem key={c.id} value={c.id}>
                    {c.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
        {errors.companyId && (
          <p className="text-xs text-destructive">{errors.companyId.message}</p>
        )}
      </div>

      <div className="space-y-2">
        <Label htmlFor="planId">Plan</Label>
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
                <SelectValue
                  placeholder={plansLoading ? "Loading plans…" : "Select a plan"}
                />
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

      <div className="space-y-2">
        <Label htmlFor="periodDays">Period (days)</Label>
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

      <Button type="submit" className="w-full" disabled={isLoading}>
        {isLoading ? (
          <>
            <Spinner className="mr-2 size-4" />
            Assigning…
          </>
        ) : (
          "Assign Subscription"
        )}
      </Button>
    </form>
  );
}
