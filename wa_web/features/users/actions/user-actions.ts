"use server";

import { createAction } from "@/lib/actions/wrapper";
import { UserService } from "@/features/users/services/user-service";
import {
  createUserSchema,
  updateUserSchema,
  resetPasswordSchema,
} from "@/features/users/schema/user-schema";
import type { UserListParams } from "@/features/users/types";

// Every user action is SuperAdmin-only — the wrapper enforces auth + role BEFORE
// the handler runs (defense-in-depth on top of the API's [Authorize]).
const SUPERADMIN = "SuperAdmin";

export const getUsersAction = createAction(
  { name: "getUsersAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (params: UserListParams) => {
    return UserService.getPaginated(params);
  }
);

export const getUserByIdAction = createAction(
  { name: "getUserByIdAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string) => {
    return UserService.getById(id);
  }
);

export const createUserAction = createAction(
  { name: "createUserAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (raw: unknown) => {
    const input = createUserSchema.parse(raw);
    return UserService.create(input);
  }
);

export const updateUserAction = createAction(
  { name: "updateUserAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string, raw: unknown) => {
    const input = updateUserSchema.parse(raw);
    return UserService.update(id, input);
  }
);

export const resetUserPasswordAction = createAction(
  { name: "resetUserPasswordAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string, raw: unknown) => {
    const input = resetPasswordSchema.parse(raw);
    return UserService.resetPassword(id, input);
  }
);

export const activateUserAction = createAction(
  { name: "activateUserAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string) => {
    return UserService.activate(id);
  }
);

export const deactivateUserAction = createAction(
  { name: "deactivateUserAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string) => {
    return UserService.deactivate(id);
  }
);
