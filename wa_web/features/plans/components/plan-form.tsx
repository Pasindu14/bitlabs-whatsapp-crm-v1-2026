"use client";

import { useEffect } from "react";
import { useForm, Controller, type Resolver } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Spinner } from "@/components/ui/spinner";
import { PERMISSIONS } from "@/features/team/permissions";
import {
  createPlanSchema,
  updatePlanSchema,
  type CreatePlanInput,
} from "@/features/plans/schema/plan-schema";

interface PlanFormProps {
  mode: "create" | "edit";
  defaultValues?: Partial<CreatePlanInput>;
  onSubmit: (data: CreatePlanInput) => void;
  isLoading: boolean;
  fieldErrors?: Record<string, string> | null;
}

export function PlanForm({
  mode,
  defaultValues,
  onSubmit,
  isLoading,
  fieldErrors,
}: PlanFormProps) {
  const {
    register,
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<CreatePlanInput>({
    resolver: zodResolver(
      mode === "create" ? createPlanSchema : updatePlanSchema
    ) as Resolver<CreatePlanInput>,
    defaultValues: {
      name: "",
      monthlyMessageQuota: 0,
      price: 0,
      currency: "USD",
      featureFlags: [],
      ...defaultValues,
    },
  });

  // Bind API field errors (e.g. duplicate name, unknown flag) back onto the inputs.
  useEffect(() => {
    if (fieldErrors) {
      for (const [field, message] of Object.entries(fieldErrors)) {
        setError(field as keyof CreatePlanInput, { message });
      }
    }
  }, [fieldErrors, setError]);

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="name">Plan Name</Label>
        <Input id="name" placeholder="Pro" {...register("name")} />
        {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-2">
          <Label htmlFor="monthlyMessageQuota">Monthly Quota</Label>
          <Input
            id="monthlyMessageQuota"
            type="number"
            min={0}
            step={1}
            placeholder="10000"
            {...register("monthlyMessageQuota")}
          />
          {errors.monthlyMessageQuota && (
            <p className="text-xs text-destructive">{errors.monthlyMessageQuota.message}</p>
          )}
        </div>

        <div className="space-y-2">
          <Label htmlFor="price">Price</Label>
          <Input
            id="price"
            type="number"
            min={0}
            step="0.01"
            placeholder="29.00"
            {...register("price")}
          />
          {errors.price && <p className="text-xs text-destructive">{errors.price.message}</p>}
        </div>
      </div>

      <div className="space-y-2">
        <Label htmlFor="currency">Currency</Label>
        <Input id="currency" maxLength={3} placeholder="USD" {...register("currency")} />
        {errors.currency && (
          <p className="text-xs text-destructive">{errors.currency.message}</p>
        )}
      </div>

      <div className="space-y-2">
        <Label>Feature Flags</Label>
        <p className="text-xs text-muted-foreground">
          Capabilities this plan unlocks for companies subscribed to it.
        </p>
        <Controller
          control={control}
          name="featureFlags"
          render={({ field }) => {
            const selected = field.value ?? [];
            const toggle = (key: string, checked: boolean) => {
              const next = checked
                ? [...selected, key]
                : selected.filter((k) => k !== key);
              field.onChange(next);
            };
            return (
              <div className="grid grid-cols-2 gap-2 rounded-md border p-3">
                {PERMISSIONS.map((perm) => (
                  <label
                    key={perm.key}
                    htmlFor={`flag-${perm.key}`}
                    className="flex items-center gap-2 text-sm"
                  >
                    <Checkbox
                      id={`flag-${perm.key}`}
                      checked={selected.includes(perm.key)}
                      onCheckedChange={(c) => toggle(perm.key, c === true)}
                    />
                    {perm.label}
                  </label>
                ))}
              </div>
            );
          }}
        />
        {errors.featureFlags && (
          <p className="text-xs text-destructive">
            {errors.featureFlags.message as string}
          </p>
        )}
      </div>

      <Button type="submit" className="w-full" disabled={isLoading}>
        {isLoading ? (
          <>
            <Spinner className="mr-2 size-4" />
            {mode === "create" ? "Creating…" : "Updating…"}
          </>
        ) : mode === "create" ? (
          "Create Plan"
        ) : (
          "Update Plan"
        )}
      </Button>
    </form>
  );
}
