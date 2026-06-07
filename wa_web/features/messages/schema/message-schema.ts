import { z } from "zod";

export const sendMessageSchema = z.object({
  contactId: z.string().uuid("Please select a contact.").optional(),
  wabaConnectionId: z.string().uuid("Please select a WhatsApp connection."),
  body: z
    .string()
    .min(1, "Message cannot be empty.")
    .max(4096, "Message cannot exceed 4096 characters."),
});

export type SendMessageInput = z.infer<typeof sendMessageSchema>;
