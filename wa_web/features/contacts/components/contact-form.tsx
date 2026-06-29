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
  type UpdateContactInput,
} from "@/features/contacts/schema/contact-schema";

const optedOutDateFmt = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  year: "numeric",
});

interface ContactFormProps {
  mode: "create" | "edit";
  defaultValues?: Partial<UpdateContactInput>;
  /** Opt-out timestamp, shown as context on the edit screen (display only). */
  optedOutAt?: string | null;
  onSubmit: (data: UpdateContactInput) => void;
  isLoading: boolean;
  fieldErrors?: Record<string, string> | null;
}

export function ContactForm({
  mode,
  defaultValues,
  optedOutAt,
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
  } = useForm<UpdateContactInput>({
    resolver: zodResolver(
      mode === "create" ? createContactSchema : updateContactSchema
    ) as Resolver<UpdateContactInput>,
    defaultValues: {
      name: "",
      phone: "",
      hasOptedIn: false,
      isOptedOut: false,
      ...defaultValues,
    },
  });

  // Bind API field errors (e.g. duplicate phone) back onto the inputs.
  useEffect(() => {
    if (fieldErrors) {
      for (const [field, message] of Object.entries(fieldErrors)) {
        setError(field as keyof UpdateContactInput, { message });
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

      {/* Opt-out is webhook-driven (customer replies STOP). The edit screen is the only place a
          company admin can lift or apply that suppression by hand. */}
      {mode === "edit" && (
        <Controller
          control={control}
          name="isOptedOut"
          render={({ field }) => (
            <label
              className={`flex items-start gap-3 rounded-md border p-3 cursor-pointer ${
                field.value ? "border-destructive/50 bg-destructive/5" : ""
              }`}
            >
              <Checkbox
                className="mt-0.5"
                checked={field.value ?? false}
                onCheckedChange={(v) => field.onChange(v === true)}
              />
              <div className="space-y-1">
                <div className="text-sm font-medium">Opted out — suppress all sending</div>
                <p className="text-xs text-muted-foreground">
                  When on, this contact replied STOP (or was suppressed) and is never messaged. Untick
                  to re-enable sending.
                  {field.value && optedOutAt
                    ? ` Opted out on ${optedOutDateFmt.format(new Date(optedOutAt))}.`
                    : ""}
                </p>
              </div>
            </label>
          )}
        />
      )}

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
