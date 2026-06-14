"use client";

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { SendMessageForm } from "@/features/messages/components/send-message-form";
import { useStartConversation } from "@/features/conversations/hooks/use-conversations";

/**
 * Starts a new conversation: pick a contact + WhatsApp number and send the first message. Reuses the
 * Messages feature's send form (contact search + connection picker). On success the new thread opens.
 *
 * Note: WhatsApp only allows free-form messages within 24h of the customer's last message. For a contact
 * who hasn't messaged you, the thread still opens but the first message is recorded as failed (closed
 * window) — initiating a cold chat requires an approved template.
 */
export function NewConversationDialog({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const start = useStartConversation();

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>New conversation</DialogTitle>
          <DialogDescription>
            Choose a contact and send the first message. You can only message a customer within 24 hours of
            their last message — otherwise an approved template is required.
          </DialogDescription>
        </DialogHeader>

        <SendMessageForm
          isLoading={start.isPending}
          onSubmit={(data) =>
            start.mutate(
              {
                contactId: data.resolvedContactId,
                wabaConnectionId: data.wabaConnectionId,
                body: data.body,
              },
              { onSuccess: () => onOpenChange(false) },
            )
          }
        />
      </DialogContent>
    </Dialog>
  );
}
