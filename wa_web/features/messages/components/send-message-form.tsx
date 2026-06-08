"use client";

import { useCallback } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { User } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { AsyncSelect } from "@/components/async-select";
import { sendMessageSchema, type SendMessageInput } from "@/features/messages/schema/message-schema";
import { useActiveWabaConnections } from "@/features/messages/hooks/use-messages";
import { searchContactsForMessageAction } from "@/features/messages/actions/message-actions";
import type { Contact } from "@/features/contacts/types";

interface SendMessageFormProps {
  preselectedContactId?: string | null;
  onSubmit: (data: SendMessageInput & { resolvedContactId: string }) => void;
  isLoading: boolean;
}

export function SendMessageForm({ preselectedContactId, onSubmit, isLoading }: SendMessageFormProps) {
  const { data: connections, isLoading: loadingConnections, isError: connectionsError } = useActiveWabaConnections();

  const {
    control,
    register,
    handleSubmit,
    watch,
    formState: { errors },
  } = useForm<SendMessageInput>({
    resolver: zodResolver(sendMessageSchema),
    defaultValues: { contactId: "", wabaConnectionId: "", body: "" },
  });

  const body = watch("body");

  const contactFetcher = useCallback(async (query?: string) => {
    const res = await searchContactsForMessageAction(query);
    return res.success ? res.data : [];
  }, []);

  const handleFormSubmit = (data: SendMessageInput) => {
    const resolvedContactId = preselectedContactId ?? data.contactId ?? "";
    if (!resolvedContactId) return;
    onSubmit({ ...data, resolvedContactId });
  };

  return (
    <form onSubmit={handleSubmit(handleFormSubmit)} className="space-y-4">
      {/* Contact selector — only shown when no contact is pre-selected */}
      {!preselectedContactId && (
        <div className="space-y-2">
          <Label>Contact</Label>
          <Controller
            name="contactId"
            control={control}
            render={({ field }) => (
              <AsyncSelect<Contact>
                label="Contact"
                placeholder="Search contacts…"
                fetcher={contactFetcher}
                value={field.value ?? ""}
                onChange={field.onChange}
                getOptionValue={(c) => c.id}
                getDisplayValue={(c) => (
                  <div className="flex items-center gap-2">
                    <User className="h-4 w-4 shrink-0 text-muted-foreground" />
                    <span className="font-medium">{c.name}</span>
                    <span className="text-muted-foreground font-mono text-xs">{c.phone}</span>
                  </div>
                )}
                renderOption={(c) => (
                  <div className="flex items-center gap-2">
                    <User className="h-4 w-4 shrink-0 text-muted-foreground" />
                    <span className="font-medium">{c.name}</span>
                    <span className="text-muted-foreground font-mono text-xs ml-1">{c.phone}</span>
                  </div>
                )}
                width="100%"
                disabled={isLoading}
              />
            )}
          />
          {errors.contactId && (
            <p className="text-xs text-destructive">{errors.contactId.message}</p>
          )}
        </div>
      )}

      <div className="space-y-2">
        <Label htmlFor="wabaConnectionId">Send from</Label>
        <Controller
          name="wabaConnectionId"
          control={control}
          render={({ field }) => (
            <Select onValueChange={field.onChange} value={field.value} disabled={isLoading}>
              <SelectTrigger id="wabaConnectionId">
                <SelectValue placeholder={
                  loadingConnections ? "Loading connections…" :
                  connectionsError ? "Failed to load connections" :
                  "Select a phone number"
                } />
              </SelectTrigger>
              <SelectContent>
                {loadingConnections && (
                  <SelectItem value="_loading" disabled>Loading…</SelectItem>
                )}
                {!loadingConnections && connections?.length === 0 && (
                  <SelectItem value="_none" disabled>No active connections found</SelectItem>
                )}
                {connections?.map((c) => (
                  <SelectItem key={c.id} value={c.id}>
                    {c.displayPhoneNumber}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
        {connectionsError && (
          <p className="text-xs text-destructive">Could not load WhatsApp connections. Make sure your company has an active connection configured.</p>
        )}
        {errors.wabaConnectionId && (
          <p className="text-xs text-destructive">{errors.wabaConnectionId.message}</p>
        )}
      </div>

      <div className="space-y-2">
        <Label htmlFor="body">Message</Label>
        <Textarea
          id="body"
          placeholder="Type your message…"
          rows={5}
          disabled={isLoading}
          {...register("body")}
        />
        <div className="flex justify-between">
          {errors.body ? (
            <p className="text-xs text-destructive">{errors.body.message}</p>
          ) : (
            <span />
          )}
          <p className="text-xs text-muted-foreground">{body?.length ?? 0} / 4096</p>
        </div>
      </div>

      <Button type="submit" className="w-full" disabled={isLoading}>
        {isLoading ? (
          <>
            <Spinner className="mr-2 size-4" />
            Sending…
          </>
        ) : (
          "Send Message"
        )}
      </Button>
    </form>
  );
}
