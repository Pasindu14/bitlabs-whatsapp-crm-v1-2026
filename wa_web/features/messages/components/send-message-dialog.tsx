"use client";

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { useSendMessageDialog } from "@/features/messages/store/message-store";
import { useSendMessage } from "@/features/messages/hooks/use-messages";
import { SendMessageForm } from "./send-message-form";
import type { SendMessageInput } from "@/features/messages/schema/message-schema";

export function SendMessageDialog() {
  const { isOpen, contactId, contactName, contactPhone, close } = useSendMessageDialog();
  const { mutate, isPending } = useSendMessage();

  const hasPreselectedContact = !!contactId;

  return (
    <Dialog open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <DialogContent className="sm:max-w-md max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>New Message</DialogTitle>
          <DialogDescription>
            {hasPreselectedContact ? (
              <>
                To: <span className="font-medium text-foreground">{contactName}</span>
                {contactPhone && (
                  <span className="ml-1 text-muted-foreground">({contactPhone})</span>
                )}
              </>
            ) : (
              "Select a contact and compose your message."
            )}
          </DialogDescription>
        </DialogHeader>

        {isOpen && (
          <SendMessageForm
            preselectedContactId={contactId}
            onSubmit={(data: SendMessageInput & { resolvedContactId: string }) =>
              mutate({ contactId: data.resolvedContactId, data })
            }
            isLoading={isPending}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}
