import { z } from "zod";

export const sendConversationMessageSchema = z.object({
  body: z
    .string()
    .trim()
    .min(1, "Message can't be empty")
    .max(4096, "Message is too long (max 4096 characters)"),
});

export type SendConversationMessageInput = z.infer<typeof sendConversationMessageSchema>;

/**
 * Start a conversation with either free text or an approved template. WhatsApp only accepts free text
 * within 24 hours of the customer's last message, so a cold contact needs the template; the API refuses
 * free text outside the window with WINDOW_CLOSED.
 */
export const startConversationSchema = z
  .object({
    contactId: z.string().min(1, "Select a contact"),
    wabaConnectionId: z.string().min(1, "Select a WhatsApp number"),
    body: z.string().trim().max(4096, "Message is too long (max 4096 characters)").optional(),
    templateId: z.string().optional(),
    /** Values for {{1}}, {{2}}… in order. */
    templateVariables: z.array(z.string().trim().max(120, "Keep each value under 120 characters")).max(20).optional(),
  })
  .refine((v) => !!v.templateId || !!v.body, { message: "Write a message or choose a template", path: ["body"] });

export type StartConversationInput = z.infer<typeof startConversationSchema>;
