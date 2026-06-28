"use client";

import { useEffect } from "react";
import { useForm, type Resolver } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import {
  createPackageSchema,
  updatePackageSchema,
  type CreatePackageInput,
} from "@/features/packages/schema/package-schema";

interface PackageFormProps {
  mode: "create" | "edit";
  defaultValues?: Partial<CreatePackageInput>;
  onSubmit: (data: CreatePackageInput) => void;
  isLoading: boolean;
  fieldErrors?: Record<string, string> | null;
}

export function PackageForm({
  mode,
  defaultValues,
  onSubmit,
  isLoading,
  fieldErrors,
}: PackageFormProps) {
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<CreatePackageInput>({
    resolver: zodResolver(
      mode === "create" ? createPackageSchema : updatePackageSchema
    ) as Resolver<CreatePackageInput>,
    defaultValues: {
      name: "",
      description: undefined,
      extraMessages: 10000,
      price: 0,
      currency: "USD",
      ...defaultValues,
    },
  });

  // Bind API field errors (e.g. duplicate name) back onto the inputs.
  useEffect(() => {
    if (fieldErrors) {
      for (const [field, message] of Object.entries(fieldErrors)) {
        setError(field as keyof CreatePackageInput, { message });
      }
    }
  }, [fieldErrors, setError]);

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="name">Package Name</Label>
        <Input id="name" placeholder="10k Top-up" {...register("name")} />
        {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
      </div>

      <div className="space-y-2">
        <Label htmlFor="description">Description</Label>
        <Input
          id="description"
          placeholder="One-off bundle of extra message credits."
          {...register("description")}
        />
        {errors.description && (
          <p className="text-xs text-destructive">{errors.description.message}</p>
        )}
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-2">
          <Label htmlFor="extraMessages">Extra Messages</Label>
          <Input
            id="extraMessages"
            type="number"
            min={1}
            step={1}
            placeholder="10000"
            {...register("extraMessages")}
          />
          {errors.extraMessages && (
            <p className="text-xs text-destructive">{errors.extraMessages.message}</p>
          )}
        </div>

        <div className="space-y-2">
          <Label htmlFor="price">Price</Label>
          <Input
            id="price"
            type="number"
            min={0}
            step="0.01"
            placeholder="20.00"
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

      <Button type="submit" className="w-full" disabled={isLoading}>
        {isLoading ? (
          <>
            <Spinner className="mr-2 size-4" />
            {mode === "create" ? "Creating…" : "Updating…"}
          </>
        ) : mode === "create" ? (
          "Create Package"
        ) : (
          "Update Package"
        )}
      </Button>
    </form>
  );
}
