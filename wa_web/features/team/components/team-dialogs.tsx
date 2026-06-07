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
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Spinner } from "@/components/ui/spinner";
import {
  useCreateTeamDialog,
  useEditTeamDialog,
  useResetTeamPasswordDialog,
  useActivateTeamDialog,
  useDeactivateTeamDialog,
} from "@/features/team/store/team-store";
import {
  useTeamMember,
  useCreateTeamMember,
  useUpdateTeamMember,
  useResetTeamPassword,
  useActivateTeamMember,
  useDeactivateTeamMember,
} from "@/features/team/hooks/use-team";
import { TeamForm } from "./team-form";
import {
  resetTeamPasswordSchema,
  type CreateTeamMemberInput,
  type UpdateTeamMemberInput,
  type ResetTeamPasswordInput,
} from "@/features/team/schema/team-schema";

function CreateTeamDialog() {
  const { isOpen, close } = useCreateTeamDialog();
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useCreateTeamMember();

  return (
    <Dialog
      open={isOpen}
      onOpenChange={(open) => {
        if (!open) {
          close();
          clearFieldErrors();
        }
      }}
    >
      <DialogContent className="sm:max-w-md max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Add User</DialogTitle>
          <DialogDescription>
            Create a Company Admin or Agent user for your company.
          </DialogDescription>
        </DialogHeader>
        <TeamForm
          mode="create"
          onSubmit={(data) => mutate(data as CreateTeamMemberInput)}
          isLoading={isPending}
          fieldErrors={fieldErrors}
        />
      </DialogContent>
    </Dialog>
  );
}

function EditTeamDialog() {
  const { isOpen, selectedId, close } = useEditTeamDialog();
  const { data: member, isLoading } = useTeamMember(isOpen ? selectedId : null);
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useUpdateTeamMember();

  return (
    <Dialog
      open={isOpen}
      onOpenChange={(open) => {
        if (!open) {
          close();
          clearFieldErrors();
        }
      }}
    >
      <DialogContent className="sm:max-w-md max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Edit User</DialogTitle>
          <DialogDescription>Update user details.</DialogDescription>
        </DialogHeader>
        {isLoading ? (
          <div className="flex items-center justify-center py-8">
            <Spinner className="size-6" />
          </div>
        ) : (
          <TeamForm
            mode="edit"
            defaultValues={
              member
                ? {
                    fullName: member.fullName,
                    email: member.email,
                    password: "",
                    role: member.role,
                    permissions: (member.permissions ?? []) as CreateTeamMemberInput["permissions"],
                  }
                : undefined
            }
            onSubmit={(data) => {
              if (!selectedId) return;
              mutate({ id: selectedId, data: data as UpdateTeamMemberInput });
            }}
            isLoading={isPending}
            fieldErrors={fieldErrors}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}

function ResetTeamPasswordDialog() {
  const { isOpen, selectedId, close } = useResetTeamPasswordDialog();
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useResetTeamPassword();

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<ResetTeamPasswordInput>({
    resolver: zodResolver(resetTeamPasswordSchema),
    defaultValues: { password: "" },
  });

  return (
    <Dialog
      open={isOpen}
      onOpenChange={(open) => {
        if (!open) {
          close();
          clearFieldErrors();
          reset();
        }
      }}
    >
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Reset Password</DialogTitle>
          <DialogDescription>
            Set a new password for this user. They will use it on their next sign-in.
          </DialogDescription>
        </DialogHeader>
        <form
          onSubmit={handleSubmit((data) => {
            if (selectedId) mutate({ id: selectedId, data });
          })}
          className="space-y-4"
        >
          <div className="space-y-2">
            <Label htmlFor="reset-password">New Password</Label>
            <Input
              id="reset-password"
              type="password"
              autoComplete="new-password"
              placeholder="At least 8 characters"
              {...register("password")}
            />
            {(errors.password || fieldErrors?.password) && (
              <p className="text-xs text-destructive">
                {errors.password?.message ?? fieldErrors?.password}
              </p>
            )}
          </div>
          <Button type="submit" className="w-full" disabled={isPending}>
            {isPending ? (
              <>
                <Spinner className="mr-2 size-4" />
                Resetting…
              </>
            ) : (
              "Reset Password"
            )}
          </Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function ActivateTeamDialog() {
  const { isOpen, selectedId, close } = useActivateTeamDialog();
  const { mutate, isPending } = useActivateTeamMember();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Activate user?</AlertDialogTitle>
          <AlertDialogDescription>
            This marks the user active and lets them sign in again.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>Cancel</AlertDialogCancel>
          <AlertDialogAction
            disabled={isPending}
            onClick={(e) => {
              e.preventDefault();
              if (selectedId) mutate(selectedId);
            }}
          >
            {isPending && <Spinner className="mr-2" />}
            Activate
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

function DeactivateTeamDialog() {
  const { isOpen, selectedId, close } = useDeactivateTeamDialog();
  const { mutate, isPending } = useDeactivateTeamMember();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Deactivate user?</AlertDialogTitle>
          <AlertDialogDescription>
            This marks the user inactive and blocks sign-in. It can be reactivated later.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>Cancel</AlertDialogCancel>
          <AlertDialogAction
            disabled={isPending}
            onClick={(e) => {
              e.preventDefault();
              if (selectedId) mutate(selectedId);
            }}
            className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
          >
            {isPending && <Spinner className="mr-2" />}
            Deactivate
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

export function TeamDialogs() {
  return (
    <>
      <CreateTeamDialog />
      <EditTeamDialog />
      <ResetTeamPasswordDialog />
      <ActivateTeamDialog />
      <DeactivateTeamDialog />
    </>
  );
}
