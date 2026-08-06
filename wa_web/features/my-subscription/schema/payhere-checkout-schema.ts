import { z } from "zod";

/** PayHere billing-details form schema. Mirrors wa_api PayHereCheckoutRequest (minus planId). */
export const payHereBillingSchema = z.object({
  firstName: z.string().trim().min(1, "First name is required"),
  lastName: z.string().trim().min(1, "Last name is required"),
  phone: z.string().trim().min(1, "Phone is required"),
  address: z.string().trim().min(1, "Address is required"),
  city: z.string().trim().min(1, "City is required"),
  country: z.string().trim().min(1, "Country is required"),
});

export type PayHereBillingInput = z.infer<typeof payHereBillingSchema>;
