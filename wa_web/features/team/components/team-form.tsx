"use client";

import { useEffect } from "react";
import { useForm, useWatch, Controller, type Resolver } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Spinner } from "@/components/ui/spinner";
import { PERMISSIONS, type PermissionKey } from "@/features/team/permissions";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  createTeamMemberSchema,
  updateTeamMemberSchema,
  TEAM_ROLES,
  type CreateTeamMemberInput,
} from "@/features/team/schema/team-schema";

const ROLE_LABELS: Record<(typeof TEAM_ROLES)[number], string> = {
  CompanyAdmin: "Company Admin",
  Agent: "Agent",
};

interface TeamFormProps {
  mode: "create" | "edit";
  defaultValues?: Partial<CreateTeamMemberInput>;
  onSubmit: (data: CreateTeamMemberInput) => void;
  isLoading: boolean;
  fieldErrors?: Record<string, string> | null;
}

export function TeamForm({
  mode,
  defaultValues,
  onSubmit,
  isLoading,
  fieldErrors,
}: TeamFormProps) {
  const {
    register,
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<CreateTeamMemberInput>({
    resolver: zodResolver(
      mode === "create" ? createTeamMemberSchema : updateTeamMemberSchema
    ) as unknown as Resolver<CreateTeamMemberInput>,
    defaultValues: {
      fullName: "",
      email: "",
      password: "",
      role: "Agent",
      permissions: [],
      ...defaultValues,
    },
  });

  // Permissions only apply to Agents — CompanyAdmins are all-access by role.
  const role = useWatch({ control, name: "role" });
  const isAgent = role === "Agent";

  // Bind API field errors (e.g. duplicate email) back onto the inputs.
  useEffect(() => {
    if (fieldErrors) {
      for (const [field, message] of Object.entries(fieldErrors)) {
        setError(field as keyof CreateTeamMemberInput, { message });
      }
    }
  }, [fieldErrors, setError]);

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
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
            <Select value={field.value ?? "Agent"} onValueChange={field.onChange}>
              <SelectTrigger id="role" className="w-full">
                <SelectValue placeholder="Select role" />
              </SelectTrigger>
              <SelectContent>
                {TEAM_ROLES.map((r) => (
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

      {isAgent && (
        <div className="space-y-2">
          <Label>Permissions</Label>
          <p className="text-xs text-muted-foreground">
            Choose what this agent can do. Company Admins always have full access.
          </p>
          <Controller
            control={control}
            name="permissions"
            render={({ field }) => {
              const selected = field.value ?? [];
              const toggle = (key: PermissionKey, checked: boolean) =>
                field.onChange(
                  checked
                    ? [...selected, key]
                    : selected.filter((k) => k !== key)
                );
              return (
                <div className="grid grid-cols-2 gap-2 rounded-md border p-3">
                  {PERMISSIONS.map((p) => (
                    <label
                      key={p.key}
                      htmlFor={`perm-${p.key}`}
                      className="flex items-center gap-2 text-sm font-normal"
                    >
                      <Checkbox
                        id={`perm-${p.key}`}
                        checked={selected.includes(p.key)}
                        onCheckedChange={(c) => toggle(p.key, c === true)}
                      />
                      {p.label}
                    </label>
                  ))}
                </div>
              );
            }}
          />
          {errors.permissions && (
            <p className="text-xs text-destructive">{errors.permissions.message}</p>
          )}
        </div>
      )}

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
