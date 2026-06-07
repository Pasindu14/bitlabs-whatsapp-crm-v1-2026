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
  createUserSchema,
  updateUserSchema,
  USER_ROLES,
  type CreateUserInput,
} from "@/features/users/schema/user-schema";
import { useActiveCompanies } from "@/features/users/hooks/use-users";

const ROLE_LABELS: Record<(typeof USER_ROLES)[number], string> = {
  CompanyAdmin: "Company Admin",
  Agent: "Agent",
};

interface UserFormProps {
  mode: "create" | "edit";
  defaultValues?: Partial<CreateUserInput>;
  onSubmit: (data: CreateUserInput) => void;
  isLoading: boolean;
  fieldErrors?: Record<string, string> | null;
}

export function UserForm({
  mode,
  defaultValues,
  onSubmit,
  isLoading,
  fieldErrors,
}: UserFormProps) {
  const { data: companies, isLoading: companiesLoading } = useActiveCompanies();

  const {
    register,
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<CreateUserInput>({
    resolver: zodResolver(
      mode === "create" ? createUserSchema : updateUserSchema
    ) as unknown as Resolver<CreateUserInput>,
    defaultValues: {
      companyId: "",
      fullName: "",
      email: "",
      password: "",
      role: "CompanyAdmin",
      ...defaultValues,
    },
  });

  // Bind API field errors (e.g. duplicate email) back onto the inputs.
  useEffect(() => {
    if (fieldErrors) {
      for (const [field, message] of Object.entries(fieldErrors)) {
        setError(field as keyof CreateUserInput, { message });
      }
    }
  }, [fieldErrors, setError]);

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
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
        <Label htmlFor="fullName">Full Name</Label>
        <Input id="fullName" placeholder="Jane Doe" {...register("fullName")} />
        {errors.fullName && <p className="text-xs text-destructive">{errors.fullName.message}</p>}
      </div>

      <div className="space-y-2">
        <Label htmlFor="email">Email</Label>
        <Input
          id="email"
          type="email"
          autoComplete="off"
          placeholder="jane@acme.com"
          {...register("email")}
        />
        {errors.email && <p className="text-xs text-destructive">{errors.email.message}</p>}
      </div>

      {mode === "create" && (
        <div className="space-y-2">
          <Label htmlFor="password">Password</Label>
          <Input
            id="password"
            type="password"
            autoComplete="new-password"
            placeholder="At least 8 characters"
            {...register("password")}
          />
          {errors.password && <p className="text-xs text-destructive">{errors.password.message}</p>}
        </div>
      )}

      <div className="space-y-2">
        <Label htmlFor="role">Role</Label>
        <Controller
          control={control}
          name="role"
          render={({ field }) => (
            <Select value={field.value ?? "CompanyAdmin"} onValueChange={field.onChange}>
              <SelectTrigger id="role" className="w-full">
                <SelectValue placeholder="Select role" />
              </SelectTrigger>
              <SelectContent>
                {USER_ROLES.map((r) => (
                  <SelectItem key={r} value={r}>
                    {ROLE_LABELS[r]}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
        {errors.role && <p className="text-xs text-destructive">{errors.role.message}</p>}
      </div>

      <Button type="submit" className="w-full" disabled={isLoading}>
        {isLoading ? (
          <>
            <Spinner className="mr-2 size-4" />
            {mode === "create" ? "Creating…" : "Updating…"}
          </>
        ) : mode === "create" ? (
          "Create User"
        ) : (
          "Update User"
        )}
      </Button>
    </form>
  );
}
