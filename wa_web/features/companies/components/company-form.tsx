"use client";

import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  createCompanySchema,
  type CreateCompanyInput,
} from "@/features/companies/schema/company-schema";

interface CompanyFormProps {
  mode: "create" | "edit";
  defaultValues?: Partial<CreateCompanyInput>;
  onSubmit: (data: CreateCompanyInput) => void;
  isLoading: boolean;
  fieldErrors?: Record<string, string> | null;
}

export function CompanyForm({
  mode,
  defaultValues,
  onSubmit,
  isLoading,
  fieldErrors,
}: CompanyFormProps) {
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<CreateCompanyInput>({
    resolver: zodResolver(createCompanySchema),
    defaultValues: {
      name: "",
      slug: "",
      email: "",
      phone: "",
      ...defaultValues,
    },
  });

  // Bind API field errors (e.g. duplicate name/slug) back onto the inputs.
  useEffect(() => {
    if (fieldErrors) {
      for (const [field, message] of Object.entries(fieldErrors)) {
        setError(field as keyof CreateCompanyInput, { message });
      }
    }
  }, [fieldErrors, setError]);

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="name">Name</Label>
        <Input id="name" placeholder="Acme Inc." {...register("name")} />
        {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
      </div>

      <div className="space-y-2">
        <Label htmlFor="slug">Slug (optional)</Label>
        <Input id="slug" placeholder="acme" {...register("slug")} />
        {errors.slug && <p className="text-xs text-destructive">{errors.slug.message}</p>}
      </div>

      <div className="space-y-2">
        <Label htmlFor="email">Email (optional)</Label>
        <Input id="email" type="email" placeholder="contact@acme.com" {...register("email")} />
        {errors.email && <p className="text-xs text-destructive">{errors.email.message}</p>}
      </div>

      <div className="space-y-2">
        <Label htmlFor="phone">Phone (optional)</Label>
        <Input id="phone" placeholder="+1 555 0100" {...register("phone")} />
        {errors.phone && <p className="text-xs text-destructive">{errors.phone.message}</p>}
      </div>

      <Button type="submit" className="w-full" disabled={isLoading}>
        {isLoading && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
        {mode === "create" ? "Create Company" : "Update Company"}
      </Button>
    </form>
  );
}
