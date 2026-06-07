"use client";

import { useEffect } from "react";
import { useForm, type Resolver } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import {
  createContactListSchema,
  updateContactListSchema,
  type CreateContactListInput,
} from "@/features/contact-lists/schema/contact-list-schema";

interface ContactListFormProps {
  mode: "create" | "edit";
  defaultValues?: Partial<CreateContactListInput>;
  onSubmit: (data: CreateContactListInput) => void;
  isLoading: boolean;
  fieldErrors?: Record<string, string> | null;
}

export function ContactListForm({
  mode,
  defaultValues,
  onSubmit,
  isLoading,
  fieldErrors,
}: ContactListFormProps) {
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<CreateContactListInput>({
    resolver: zodResolver(
      mode === "create" ? createContactListSchema : updateContactListSchema
    ) as Resolver<CreateContactListInput>,
    defaultValues: {
      name: "",
      description: "",
      ...defaultValues,
    },
  });

  useEffect(() => {
    if (fieldErrors) {
      for (const [field, message] of Object.entries(fieldErrors)) {
        setError(field as keyof CreateContactListInput, { message });
      }
    }
  }, [fieldErrors, setError]);

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="name">Name</Label>
        <Input id="name" placeholder="VIP Customers" {...register("name")} />
        {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
      </div>

      <div className="space-y-2">
        <Label htmlFor="description">Description (optional)</Label>
        <Input id="description" placeholder="High-value customers" {...register("description")} />
        {errors.description && (
          <p className="text-xs text-destructive">{errors.description.message}</p>
        )}
      </div>

      <Button type="submit" className="w-full" disabled={isLoading}>
        {isLoading ? (
          <>
            <Spinner className="mr-2 size-4" />
            {mode === "create" ? "Creating…" : "Updating…"}
          </>
        ) : mode === "create" ? (
          "Create List"
        ) : (
          "Update List"
        )}
      </Button>
    </form>
  );
}
