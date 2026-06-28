"use client";

import { useEffect } from "react";
import { useForm, Controller, type Resolver } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { Checkbox } from "@/components/ui/checkbox";
import {
  createContactSchema,
  updateContactSchema,
  type CreateContactInput,
} from "@/features/contacts/schema/contact-schema";

interface ContactFormProps {
  mode: "create" | "edit";
  defaultValues?: Partial<CreateContactInput>;
  onSubmit: (data: CreateContactInput) => void;
  isLoading: boolean;
  fieldErrors?: Record<string, string> | null;
}

export function ContactForm({
  mode,
  defaultValues,
  onSubmit,
  isLoading,
  fieldErrors,
}: ContactFormProps) {
  const {
    register,
    handleSubmit,
    control,
    setError,
    formState: { errors },
  } = useForm<CreateContactInput>({
    resolver: zodResolver(
      mode === "create" ? createContactSchema : updateContactSchema
    ) as Resolver<CreateContactInput>,
    defaultValues: {
      name: "",
      phone: "",
      hasOptedIn: false,
      ...defaultValues,
    },
  });

  // Bind API field errors (e.g. duplicate phone) back onto the inputs.
  useEffect(() => {
    if (fieldErrors) {
      for (const [field, message] of Object.entries(fieldErrors)) {
        setError(field as keyof CreateContactInput, { message });
      }
    }
  }, [fieldErrors, setError]);

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="name">Name</Label>
        <Input id="name" placeholder="Jane Doe" {...register("name")} />
        {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
      </div>

      <div className="space-y-2">
        <Label htmlFor="phone">Phone Number</Label>
        <Input id="phone" placeholder="94771234567" inputMode="tel" {...register("phone")} />
        {errors.phone ? (
          <p className="text-xs text-destructive">{errors.phone.message}</p>
        ) : (
          <p className="text-xs text-muted-foreground">
            Digits only, country code first (e.g. 94771234567).
          </p>
        )}
      </div>

      <Controller
        control={control}
        name="hasOptedIn"
        render={({ field }) => (
          <label className="flex items-start gap-3 rounded-md border p-3 cursor-pointer">
            <Checkbox
              className="mt-0.5"
              checked={field.value ?? false}
              onCheckedChange={(v) => field.onChange(v === true)}
            />
            <div className="space-y-1">
              <div className="text-sm font-medium">Opted in to messaging</div>
              <p className="text-xs text-muted-foreground">
                Tick only if this contact has given consent to receive business-initiated messages.
                Recorded as a manual opt-in.
              </p>
            </div>
          </label>
        )}
      />

      <Button type="submit" className="w-full" disabled={isLoading}>
        {isLoading ? (
          <>
            <Spinner className="mr-2 size-4" />
            {mode === "create" ? "Adding…" : "Updating…"}
          </>
        ) : mode === "create" ? (
          "Add Contact"
        ) : (
          "Update Contact"
        )}
      </Button>
    </form>
  );
}
