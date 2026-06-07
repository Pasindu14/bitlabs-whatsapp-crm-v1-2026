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
  createWabaConnectionSchema,
  updateWabaConnectionSchema,
  WABA_CONNECTION_STATUSES,
  type CreateWabaConnectionInput,
} from "@/features/waba-connections/schema/waba-connection-schema";
import { useActiveCompanies } from "@/features/waba-connections/hooks/use-waba-connections";

interface WabaConnectionFormProps {
  mode: "create" | "edit";
  defaultValues?: Partial<CreateWabaConnectionInput>;
  onSubmit: (data: CreateWabaConnectionInput) => void;
  isLoading: boolean;
  fieldErrors?: Record<string, string> | null;
}

export function WabaConnectionForm({
  mode,
  defaultValues,
  onSubmit,
  isLoading,
  fieldErrors,
}: WabaConnectionFormProps) {
  const { data: companies, isLoading: companiesLoading } = useActiveCompanies();

  const {
    register,
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<CreateWabaConnectionInput>({
    resolver: zodResolver(
      mode === "create" ? createWabaConnectionSchema : updateWabaConnectionSchema
    ) as Resolver<CreateWabaConnectionInput>,
    defaultValues: {
      companyId: "",
      phoneNumberId: "",
      wabaId: "",
      displayPhoneNumber: "",
      accessToken: "",
      status: "Connected",
      ...defaultValues,
    },
  });

  // Bind API field errors (e.g. duplicate phone number id) back onto the inputs.
  useEffect(() => {
    if (fieldErrors) {
      for (const [field, message] of Object.entries(fieldErrors)) {
        setError(field as keyof CreateWabaConnectionInput, { message });
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
        <Label htmlFor="phoneNumberId">Phone Number ID</Label>
        <Input id="phoneNumberId" placeholder="123456789012345" {...register("phoneNumberId")} />
        {errors.phoneNumberId && (
          <p className="text-xs text-destructive">{errors.phoneNumberId.message}</p>
        )}
      </div>

      <div className="space-y-2">
        <Label htmlFor="wabaId">WABA ID</Label>
        <Input id="wabaId" placeholder="987654321098765" {...register("wabaId")} />
        {errors.wabaId && <p className="text-xs text-destructive">{errors.wabaId.message}</p>}
      </div>

      <div className="space-y-2">
        <Label htmlFor="displayPhoneNumber">Display Phone Number (optional)</Label>
        <Input id="displayPhoneNumber" placeholder="+1 555 0100" {...register("displayPhoneNumber")} />
        {errors.displayPhoneNumber && (
          <p className="text-xs text-destructive">{errors.displayPhoneNumber.message}</p>
        )}
      </div>

      <div className="space-y-2">
        <Label htmlFor="accessToken">
          Access Token{mode === "edit" ? " (leave blank to keep current)" : ""}
        </Label>
        <Input
          id="accessToken"
          type="password"
          autoComplete="off"
          placeholder={mode === "edit" ? "••••••••" : "EAAG..."}
          {...register("accessToken")}
        />
        {errors.accessToken && (
          <p className="text-xs text-destructive">{errors.accessToken.message}</p>
        )}
      </div>

      <div className="space-y-2">
        <Label htmlFor="status">Status</Label>
        <Controller
          control={control}
          name="status"
          render={({ field }) => (
            <Select value={field.value ?? "Connected"} onValueChange={field.onChange}>
              <SelectTrigger id="status" className="w-full">
                <SelectValue placeholder="Select status" />
              </SelectTrigger>
              <SelectContent>
                {WABA_CONNECTION_STATUSES.map((s) => (
                  <SelectItem key={s} value={s}>
                    {s}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
        {errors.status && <p className="text-xs text-destructive">{errors.status.message}</p>}
      </div>

      <Button type="submit" className="w-full" disabled={isLoading}>
        {isLoading ? (
          <>
            <Spinner className="mr-2 size-4" />
            {mode === "create" ? "Creating…" : "Updating…"}
          </>
        ) : mode === "create" ? (
          "Create Connection"
        ) : (
          "Update Connection"
        )}
      </Button>
    </form>
  );
}
