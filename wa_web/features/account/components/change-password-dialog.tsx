"use client";

import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Spinner } from "@/components/ui/spinner";
import { useChangePassword } from "@/features/account/hooks/use-change-password";
import {
  changePasswordSchema,
  type ChangePasswordInput,
} from "@/features/account/schema/change-password-schema";

export function ChangePasswordDialog({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useChangePassword(() =>
    onOpenChange(false)
  );

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<ChangePasswordInput>({
    resolver: zodResolver(changePasswordSchema),
    defaultValues: { currentPassword: "", newPassword: "", confirmPassword: "" },
  });

  return (
    <Dialog
      open={open}
      onOpenChange={(o) => {
        if (!o) {
          clearFieldErrors();
          reset();
        }
        onOpenChange(o);
      }}
    >
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Change password</DialogTitle>
          <DialogDescription>
            Enter your current password and choose a new one.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={handleSubmit((data) => mutate(data))} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="currentPassword">Current password</Label>
            <Input
              id="currentPassword"
              type="password"
              autoComplete="current-password"
              {...register("currentPassword")}
            />
            {(errors.currentPassword || fieldErrors?.currentPassword) && (
              <p className="text-xs text-destructive">
                {errors.currentPassword?.message ?? fieldErrors?.currentPassword}
              </p>
            )}
          </div>
          <div className="space-y-2">
            <Label htmlFor="newPassword">New password</Label>
            <Input
              id="newPassword"
              type="password"
              autoComplete="new-password"
              placeholder="At least 8 characters"
              {...register("newPassword")}
            />
            {(errors.newPassword || fieldErrors?.newPassword) && (
              <p className="text-xs text-destructive">
                {errors.newPassword?.message ?? fieldErrors?.newPassword}
              </p>
            )}
          </div>
          <div className="space-y-2">
            <Label htmlFor="confirmPassword">Confirm new password</Label>
            <Input
              id="confirmPassword"
              type="password"
              autoComplete="new-password"
              {...register("confirmPassword")}
            />
            {errors.confirmPassword && (
              <p className="text-xs text-destructive">{errors.confirmPassword.message}</p>
            )}
          </div>
          <Button type="submit" className="w-full" disabled={isPending}>
            {isPending ? (
              <>
                <Spinner className="mr-2 size-4" />
                Saving…
              </>
            ) : (
              "Change password"
            )}
          </Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}
