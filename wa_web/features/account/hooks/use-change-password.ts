"use client";

import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { toast } from "sonner";
import { handleErrorToast } from "@/lib/hooks/use-error-toast";
import type { ActionFailure } from "@/lib/types/actions";
import { changePasswordAction } from "@/features/account/actions/account-actions";
import type { ChangePasswordInput } from "@/features/account/schema/change-password-schema";

export function useChangePassword(onSuccess?: () => void) {
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async (data: ChangePasswordInput) => {
      const res = await changePasswordAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      setFieldErrors(null);
      toast.success("Password changed successfully");
      onSuccess?.();
    },
    onError: (error: ActionFailure) => {
      // Surface a wrong current password under its own field for clarity.
      if (error.code === "AUTH_INVALID_CREDENTIALS") {
        setFieldErrors({ currentPassword: error.error });
      } else if (error.fields) {
        setFieldErrors(error.fields);
      }
      handleErrorToast(error, "password", "update");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}
