"use client";

import { useForm, type Resolver } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import {
  payHereBillingSchema,
  type PayHereBillingInput,
} from "@/features/my-subscription/schema/payhere-checkout-schema";

interface PayHereBillingDetailsDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  planName: string;
  defaultValues?: Partial<PayHereBillingInput>;
  onConfirm: (data: PayHereBillingInput) => void;
  isLoading: boolean;
}

/**
 * Collects the billing fields PayHere requires (first/last name, phone, address, city, country)
 * before starting checkout. Not persisted anywhere — the app has no billing-profile fields on
 * Company/User today, so this is collected fresh each time and sent straight to the backend.
 */
export function PayHereBillingDetailsDialog({
  open,
  onOpenChange,
  planName,
  defaultValues,
  onConfirm,
  isLoading,
}: PayHereBillingDetailsDialogProps) {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<PayHereBillingInput>({
    resolver: zodResolver(payHereBillingSchema) as Resolver<PayHereBillingInput>,
    defaultValues: {
      firstName: "",
      lastName: "",
      phone: "",
      address: "",
      city: "",
      country: "",
      ...defaultValues,
    },
  });

  return (
    <Dialog open={open} onOpenChange={(next) => !isLoading && onOpenChange(next)}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Billing details</DialogTitle>
          <DialogDescription>
            PayHere requires these to process your payment for the {planName} plan.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit(onConfirm)} className="space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-2">
              <Label htmlFor="firstName">First name</Label>
              <Input id="firstName" {...register("firstName")} />
              {errors.firstName && (
                <p className="text-xs text-destructive">{errors.firstName.message}</p>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="lastName">Last name</Label>
              <Input id="lastName" {...register("lastName")} />
              {errors.lastName && (
                <p className="text-xs text-destructive">{errors.lastName.message}</p>
              )}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="phone">Phone</Label>
            <Input id="phone" {...register("phone")} />
            {errors.phone && <p className="text-xs text-destructive">{errors.phone.message}</p>}
          </div>

          <div className="space-y-2">
            <Label htmlFor="address">Address</Label>
            <Input id="address" {...register("address")} />
            {errors.address && <p className="text-xs text-destructive">{errors.address.message}</p>}
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-2">
              <Label htmlFor="city">City</Label>
              <Input id="city" {...register("city")} />
              {errors.city && <p className="text-xs text-destructive">{errors.city.message}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="country">Country</Label>
              <Input id="country" {...register("country")} />
              {errors.country && (
                <p className="text-xs text-destructive">{errors.country.message}</p>
              )}
            </div>
          </div>

          <DialogFooter>
            <Button type="submit" className="w-full" disabled={isLoading}>
              {isLoading ? (
                <>
                  <Spinner className="mr-2 size-4" />
                  Starting checkout…
                </>
              ) : (
                "Continue to payment"
              )}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
