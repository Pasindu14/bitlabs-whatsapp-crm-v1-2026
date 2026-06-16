"use server";

import { createAction } from "@/lib/actions/wrapper";
import { CampaignService } from "@/features/campaigns/services/campaign-service";
import {
  createCampaignSchema,
  updateCampaignSchema,
} from "@/features/campaigns/schema/campaign-schema";
import type { CampaignListParams } from "@/features/campaigns/types";

const COMPANY_ADMIN = "CompanyAdmin";

export const getCampaignsAction = createAction(
  { name: "getCampaignsAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (params: CampaignListParams) => CampaignService.getPaginated(params)
);

export const getCampaignByIdAction = createAction(
  { name: "getCampaignByIdAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => CampaignService.getById(id)
);

export const createCampaignAction = createAction(
  { name: "createCampaignAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (raw: unknown) => {
    const input = createCampaignSchema.parse(raw);
    return CampaignService.create(input);
  }
);

export const updateCampaignAction = createAction(
  { name: "updateCampaignAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string, raw: unknown) => {
    const input = updateCampaignSchema.parse(raw);
    return CampaignService.update(id, input);
  }
);

export const deleteCampaignAction = createAction(
  { name: "deleteCampaignAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => CampaignService.delete(id)
);

export const launchCampaignAction = createAction(
  { name: "launchCampaignAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => CampaignService.launch(id)
);

export const pauseCampaignAction = createAction(
  { name: "pauseCampaignAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => CampaignService.pause(id)
);

export const resumeCampaignAction = createAction(
  { name: "resumeCampaignAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => CampaignService.resume(id)
);

export const cancelCampaignAction = createAction(
  { name: "cancelCampaignAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => CampaignService.cancel(id)
);

export const getCampaignStatsAction = createAction(
  { name: "getCampaignStatsAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => CampaignService.getStats(id)
);

export const duplicateCampaignAction = createAction(
  { name: "duplicateCampaignAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => CampaignService.duplicate(id)
);
