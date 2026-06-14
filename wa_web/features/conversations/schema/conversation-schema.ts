import { z } from "zod";

export const sendConversationMessageSchema = z.object({
  body: z
    .string()
    .trim()
    .min(1, "Message can't be empty")
    .max(4096, "Message is too long (max 4096 characters)"),
});

export type SendConversationMessageInput = z.infer<typeof sendConversationMessageSchema>;

export const startConversationSchema = z.object({
  contactId: z.string().min(1, "Select a contact"),
  wabaConnectionId: z.string().min(1, "Select a WhatsApp number"),
  body: z
    .string()
    .trim()
    .min(1, "Message can't be empty")
    .max(4096, "Message is too long (max 4096 characters)"),
});

export type StartConversationInput = z.infer<typeof startConversationSchema>;
