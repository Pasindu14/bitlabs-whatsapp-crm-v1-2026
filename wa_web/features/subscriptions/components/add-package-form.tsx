"use client";

import { useEffect } from "react";
import { useForm, Controller, type Resolver } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
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
  addPackageSchema,
  type AddPackageInput,
} from "@/features/subscriptions/schema/subscription-schema";
import {
  usePackageOptions,
  usePackageHistory,
} from "@/features/subscriptions/hooks/use-subscriptions";

const numFmt = new Intl.NumberFormat("en-US");

function formatPrice(price: number, currency: string): string {
  try {
    return new Intl.NumberFormat("en-US", { style: "currency", currency }).format(price);
  } catch {
    return `${numFmt.format(price)} ${currency}`;
  }
}

interface AddPackageFormProps {
  companyId: string | null;
  onSubmit: (data: AddPackageInput) => void;
  isLoading: boolean;
  fieldErrors?: Record<string, string> | null;
}

export function AddPackageForm({
  companyId,
  onSubmit,
  isLoading,
  fieldErrors,
}: AddPackageFormProps) {
  const { data: packages, isLoading: packagesLoading } = usePackageOptions();
  const { data: history } = usePackageHistory(companyId);

  const {
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<AddPackageInput>({
    resolver: zodResolver(addPackageSchema) as Resolver<AddPackageInput>,
    defaultValues: { packageId: "" },
  });

  useEffect(() => {
    if (fieldErrors) {
      for (const [field, message] of Object.entries(fieldErrors)) {
        setError(field as keyof AddPackageInput, { message });
      }
    }
  }, [fieldErrors, setError]);

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="packageId">Package</Label>
        <Controller
          control={control}
          name="packageId"
          render={({ field }) => (
            <Select
              value={field.value || undefined}
              onValueChange={field.onChange}
              disabled={packagesLoading}
            >
              <SelectTrigger id="packageId" className="w-full">
                <SelectValue
                  placeholder={packagesLoading ? "Loading packages…" : "Select a package"}
                />
              </SelectTrigger>
              <SelectContent>
                {(packages ?? []).map((p) => (
                  <SelectItem key={p.id} value={p.id}>
                    {p.name} — +{numFmt.format(p.extraMessages)} msgs (
                    {formatPrice(p.price, p.currency)})
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
        {errors.packageId && (
          <p className="text-xs text-destructive">{errors.packageId.message}</p>
        )}
        <p className="text-xs text-muted-foreground">
          The package&apos;s credits are added on top of the company&apos;s monthly plan quota.
        </p>
      </div>

      {history && history.length > 0 && (
        <div className="space-y-2">
          <Label>Recently added</Label>
          <ul className="max-h-32 space-y-1 overflow-y-auto rounded-md border p-2 text-xs">
            {history.slice(0, 5).map((h) => (
              <li key={h.id} className="flex items-center justify-between gap-2">
                <span className="truncate text-muted-foreground">{h.packageName}</span>
                <span className="shrink-0">+{numFmt.format(h.messagesAdded)} msgs</span>
              </li>
            ))}
          </ul>
        </div>
      )}

      <Button type="submit" className="w-full" disabled={isLoading}>
        {isLoading ? (
          <>
            <Spinner className="mr-2 size-4" />
            Adding…
          </>
        ) : (
          "Add Package"
        )}
      </Button>
    </form>
  );
}
