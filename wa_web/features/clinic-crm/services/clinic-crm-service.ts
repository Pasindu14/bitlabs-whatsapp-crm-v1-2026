import client, { type ApiSuccessBody } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type {
  ClinicCrmSettings,
  ClinicCrmTestResult,
  ClinicCrmTreatment,
} from "@/features/clinic-crm/types";
import type {
  saveClinicCrmConnectionSchema,
  SaveClinicCrmMappingsInput,
} from "@/features/clinic-crm/schema/clinic-crm-schema";
import type { z } from "zod";

type SaveConnectionPayload = z.output<typeof saveClinicCrmConnectionSchema>;

/**
 * Talks to wa_api /api/v1/clinic-crm (plan 014). One connection per company, so — like the bot
 * profile — it is a PUT and the client never has to know whether one already exists.
 */
export const ClinicCrmService = {
  async get(): Promise<ClinicCrmSettings> {
    return executeService({ context: "ClinicCrmService", method: "get" }, async () => {
      const res = await client.get<ApiSuccessBody<ClinicCrmSettings>>("/api/v1/clinic-crm");
      return res.data.data;
    });
  },

  async save(input: SaveConnectionPayload): Promise<ClinicCrmSettings> {
    return executeService({ context: "ClinicCrmService", method: "save" }, async () => {
      const res = await client.put<ApiSuccessBody<ClinicCrmSettings>>("/api/v1/clinic-crm", input);
      return res.data.data;
    });
  },

  async remove(): Promise<void> {
    return executeService({ context: "ClinicCrmService", method: "remove" }, async () => {
      await client.delete("/api/v1/clinic-crm");
    });
  },

  async test(): Promise<ClinicCrmTestResult> {
    return executeService({ context: "ClinicCrmService", method: "test" }, async () => {
      const res = await client.post<ApiSuccessBody<ClinicCrmTestResult>>("/api/v1/clinic-crm/test");
      return res.data.data;
    });
  },

  async getTreatments(): Promise<ClinicCrmTreatment[]> {
    return executeService({ context: "ClinicCrmService", method: "getTreatments" }, async () => {
      const res = await client.get<ApiSuccessBody<ClinicCrmTreatment[]>>("/api/v1/clinic-crm/treatments");
      return res.data.data ?? [];
    });
  },

  async saveMappings(input: SaveClinicCrmMappingsInput): Promise<ClinicCrmSettings> {
    return executeService({ context: "ClinicCrmService", method: "saveMappings" }, async () => {
      const res = await client.put<ApiSuccessBody<ClinicCrmSettings>>("/api/v1/clinic-crm/mappings", input);
      return res.data.data;
    });
  },
};
