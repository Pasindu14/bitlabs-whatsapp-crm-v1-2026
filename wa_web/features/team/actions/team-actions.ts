"use server";

import { createAction } from "@/lib/actions/wrapper";
import { TeamService } from "@/features/team/services/team-service";
import {
  createTeamMemberSchema,
  updateTeamMemberSchema,
  resetTeamPasswordSchema,
} from "@/features/team/schema/team-schema";
import type { TeamListParams } from "@/features/team/types";

// Every team action is CompanyAdmin-only — the wrapper enforces auth + role BEFORE
// the handler runs (defense-in-depth on top of the API's [Authorize] + JWT scoping).
const COMPANY_ADMIN = "CompanyAdmin";

export const getTeamAction = createAction(
  { name: "getTeamAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (params: TeamListParams) => {
    return TeamService.getPaginated(params);
  }
);

export const getTeamMemberByIdAction = createAction(
  { name: "getTeamMemberByIdAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => {
    return TeamService.getById(id);
  }
);

export const createTeamMemberAction = createAction(
  { name: "createTeamMemberAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (raw: unknown) => {
    const input = createTeamMemberSchema.parse(raw);
    return TeamService.create(input);
  }
);

export const updateTeamMemberAction = createAction(
  { name: "updateTeamMemberAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string, raw: unknown) => {
    const input = updateTeamMemberSchema.parse(raw);
    return TeamService.update(id, input);
  }
);

export const resetTeamPasswordAction = createAction(
  { name: "resetTeamPasswordAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string, raw: unknown) => {
    const input = resetTeamPasswordSchema.parse(raw);
    return TeamService.resetPassword(id, input);
  }
);

export const activateTeamMemberAction = createAction(
  { name: "activateTeamMemberAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => {
    return TeamService.activate(id);
  }
);

export const deactivateTeamMemberAction = createAction(
  { name: "deactivateTeamMemberAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => {
    return TeamService.deactivate(id);
  }
);
