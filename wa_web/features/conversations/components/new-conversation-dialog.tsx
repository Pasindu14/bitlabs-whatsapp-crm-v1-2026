"use client";

import { useCallback, useMemo, useState } from "react";
import { Clock, FileText, MessageSquareText, User } from "lucide-react";
import { AsyncSelect } from "@/components/async-select";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { Textarea } from "@/components/ui/textarea";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import { cn } from "@/lib/utils";
import type { Contact } from "@/features/contacts/types";
import { useActiveWabaConnections } from "@/features/messages/hooks/use-messages";
import { searchContactsForMessageAction } from "@/features/messages/actions/message-actions";
import { useApprovedTemplates } from "@/features/templates/hooks/use-templates";
import { useConversations, useStartConversation } from "@/features/conversations/hooks/use-conversations";

type Mode = "template" | "message";

/** Highest {{n}} in a template body — how many values it needs. */
function variableCount(text: string): number {
  let max = 0;
  for (const m of text.matchAll(/\{\{(\d+)\}\}/g)) max = Math.max(max, Number(m[1]));
  return max;
}

/** Template text with the values filled in; unfilled ones stay visible as {{n}}. */
function render(text: string, values: string[]): string {
  return text.replace(/\{\{(\d+)\}\}/g, (all, n) => values[Number(n) - 1]?.trim() || all);
}

/**
 * Start a conversation. WhatsApp only lets a business send free text within 24 hours of the customer's
 * last message; anyone else has to be reached with an approved template. So this opens on a template
 * unless the chosen customer's window is open, and only then offers plain text.
 */
export function NewConversationDialog({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const start = useStartConversation();
  const { data: connections, isLoading: loadingConnections } = useActiveWabaConnections();
  const { data: templates, isLoading: loadingTemplates } = useApprovedTemplates();
  // Same list (and cache) the inbox shows — enough to know whether this customer's window is open.
  const { data: conversations } = useConversations("");

  const [contactId, setContactId] = useState("");
  /** Contacts the search has returned, so the chosen one's name is at hand for {{1}}. */
  const [known, setKnown] = useState<Record<string, Contact>>({});
  const [chosenWabaId, setChosenWabaId] = useState("");
  /** The mode the user picked; null means "whatever fits the window". */
  const [chosenMode, setChosenMode] = useState<Mode | null>(null);
  const [templateId, setTemplateId] = useState("");
  /** Values the user typed, by position. Unset positions fall back to a suggestion. */
  const [edited, setEdited] = useState<Record<number, string>>({});
  const [body, setBody] = useState("");

  // One number? It's the only choice, so it's chosen.
  const wabaId = chosenWabaId || (connections?.length === 1 ? connections[0].id : "");
  const contact = contactId ? known[contactId] : undefined;

  const close = (next: boolean) => {
    if (!next) {
      setContactId("");
      setChosenWabaId("");
      setChosenMode(null);
      setTemplateId("");
      setEdited({});
      setBody("");
    }
    onOpenChange(next);
  };

  const windowOpen = useMemo(() => {
    if (!contactId || !wabaId) return false;
    const conv = conversations?.find((c) => c.contactId === contactId && c.wabaConnectionId === wabaId);
    return !!conv?.windowExpiresAt && new Date(conv.windowExpiresAt) > new Date();
  }, [conversations, contactId, wabaId]);

  // Outside the window only a template works; inside it, plain text is the natural default.
  const mode: Mode = !windowOpen ? "template" : (chosenMode ?? "message");

  // Templates belong to the number Meta approved them under.
  const numberTemplates = useMemo(
    () => (templates ?? []).filter((t) => !wabaId || t.wabaConnectionId === wabaId),
    [templates, wabaId],
  );
  const template = numberTemplates.find((t) => t.id === templateId);
  const needed = template ? variableCount(template.components.body.text) : 0;

  // {{1}} is the customer's first name across the starter library — suggest it until they type.
  const firstName = contact?.name?.trim().split(/\s+/)[0] ?? "";
  const values = Array.from({ length: needed }, (_, i) => edited[i] ?? (i === 0 ? firstName : ""));

  const contactFetcher = useCallback(async (query?: string) => {
    const res = await searchContactsForMessageAction(query);
    const items = res.success ? res.data : [];
    setKnown((prev) => ({ ...prev, ...Object.fromEntries(items.map((c) => [c.id, c])) }));
    return items;
  }, []);

  const valuesFilled = values.every((v) => v.trim().length > 0);
  const canSend =
    !!contactId &&
    !!wabaId &&
    !start.isPending &&
    (mode === "template" ? !!template && valuesFilled : windowOpen && body.trim().length > 0);

  const submit = () => {
    if (!canSend) return;
    start.mutate(
      mode === "template"
        ? { contactId, wabaConnectionId: wabaId, templateId, templateVariables: values.map((v) => v.trim()) }
        : { contactId, wabaConnectionId: wabaId, body: body.trim() },
      { onSuccess: () => close(false) },
    );
  };

  return (
    <Dialog open={open} onOpenChange={close}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>New chat</DialogTitle>
          <DialogDescription>
            Start a WhatsApp conversation. Customers who haven&apos;t messaged you in the last 24 hours can only
            be reached with an approved template.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-2">
            <Label>Customer</Label>
            <AsyncSelect<Contact>
              label="Customer"
              placeholder="Search by name or number"
              fetcher={contactFetcher}
              value={contactId}
              onChange={(id) => {
                setContactId(id);
                setChosenMode(null);
              }}
              getOptionValue={(c) => c.id}
              getDisplayValue={(c) => (
                <span className="flex items-center gap-2">
                  <User className="size-4 shrink-0 text-muted-foreground" />
                  <span className="font-medium">{c.name}</span>
                  <span className="text-xs text-muted-foreground tabular-nums">{c.phone}</span>
                </span>
              )}
              renderOption={(c) => (
                <span className="flex items-center gap-2">
                  <User className="size-4 shrink-0 text-muted-foreground" />
                  <span className="font-medium">{c.name}</span>
                  <span className="ml-1 text-xs text-muted-foreground tabular-nums">{c.phone}</span>
                </span>
              )}
              width="100%"
              disabled={start.isPending}
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="new-chat-number">Send from</Label>
            <Select
              value={wabaId}
              onValueChange={(v) => {
                setChosenWabaId(v);
                setChosenMode(null);
                setTemplateId("");
                setEdited({});
              }}
              disabled={start.isPending}
            >
              <SelectTrigger id="new-chat-number" className="w-full">
                <SelectValue placeholder={loadingConnections ? "Loading numbers…" : "Choose a WhatsApp number"} />
              </SelectTrigger>
              <SelectContent>
                {connections?.map((c) => (
                  <SelectItem key={c.id} value={c.id}>
                    {c.displayPhoneNumber}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {contactId && wabaId && (
            <p
              className={cn(
                "flex items-start gap-2 rounded-lg px-3 py-2 text-[13px]",
                windowOpen ? "bg-primary/10 text-primary" : "bg-muted text-muted-foreground",
              )}
            >
              <Clock className="mt-0.5 size-3.5 shrink-0" />
              {windowOpen
                ? "This customer messaged you in the last 24 hours, so you can write freely or use a template."
                : "This customer hasn't messaged you in the last 24 hours. WhatsApp only allows an approved template."}
            </p>
          )}

          <ToggleGroup
            type="single"
            value={mode}
            onValueChange={(v) => v && setChosenMode(v as Mode)}
            className="grid w-full grid-cols-2 gap-1 rounded-lg bg-muted p-1"
            aria-label="What to send"
          >
            <ToggleGroupItem
              value="template"
              className="h-9 gap-2 rounded-md border-0 text-[13px] font-normal data-[state=on]:bg-card data-[state=on]:font-medium data-[state=on]:shadow-[0_1px_2px_rgb(0_0_0/0.08)]"
            >
              <FileText className="size-4" /> Template
            </ToggleGroupItem>
            <ToggleGroupItem
              value="message"
              disabled={!windowOpen}
              title={windowOpen ? undefined : "Only possible within 24 hours of the customer's last message"}
              className="h-9 gap-2 rounded-md border-0 text-[13px] font-normal data-[state=on]:bg-card data-[state=on]:font-medium data-[state=on]:shadow-[0_1px_2px_rgb(0_0_0/0.08)]"
            >
              <MessageSquareText className="size-4" /> Message
            </ToggleGroupItem>
          </ToggleGroup>

          {mode === "template" ? (
            <div className="space-y-3">
              <div className="space-y-2">
                <Label htmlFor="new-chat-template">Approved template</Label>
                <Select
                  value={templateId}
                  onValueChange={(v) => {
                    setTemplateId(v);
                    setEdited({});
                  }}
                  disabled={!wabaId || start.isPending}
                >
                  <SelectTrigger id="new-chat-template" className="w-full">
                    <SelectValue
                      placeholder={
                        !wabaId
                          ? "Choose a number first"
                          : loadingTemplates
                            ? "Loading templates…"
                            : numberTemplates.length === 0
                              ? "No approved templates for this number"
                              : "Choose a template"
                      }
                    />
                  </SelectTrigger>
                  <SelectContent>
                    {numberTemplates.map((t) => (
                      <SelectItem key={t.id} value={t.id}>
                        {t.name}
                        <span className="ml-1.5 text-muted-foreground">{t.language}</span>
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                {wabaId && !loadingTemplates && numberTemplates.length === 0 && (
                  <p className="text-[12.5px] text-muted-foreground">
                    Create one under Templates and wait for Meta to approve it. Templates with an image or
                    button link are sent from Campaigns.
                  </p>
                )}
              </div>

              {template && needed > 0 && (
                <div className="grid gap-2.5 sm:grid-cols-2">
                  {Array.from({ length: needed }, (_, i) => (
                    <div key={i} className="space-y-1.5">
                      <Label htmlFor={`new-chat-var-${i}`} className="text-[12.5px]">
                        {`{{${i + 1}}}`}
                        {i === 0 && <span className="ml-1 font-normal text-muted-foreground">usually the first name</span>}
                      </Label>
                      <Input
                        id={`new-chat-var-${i}`}
                        value={values[i] ?? ""}
                        maxLength={120}
                        onChange={(e) => setEdited((prev) => ({ ...prev, [i]: e.target.value }))}
                        disabled={start.isPending}
                      />
                    </div>
                  ))}
                </div>
              )}

              {template && (
                <div className="rounded-xl bg-[#eef4ef] p-3 dark:bg-muted/40">
                  <p className="mb-1.5 text-[12px] text-muted-foreground">What the customer will see</p>
                  <p className="ml-auto max-w-[90%] rounded-2xl rounded-br-md bg-[#dcf8c6] px-3.5 py-2 text-[14px] leading-relaxed whitespace-pre-wrap text-[#14201a] dark:bg-[#12432a] dark:text-[#e8efe9]">
                    {render(template.components.body.text, values)}
                  </p>
                </div>
              )}
            </div>
          ) : (
            <div className="space-y-2">
              <Label htmlFor="new-chat-body">Message</Label>
              <Textarea
                id="new-chat-body"
                rows={4}
                value={body}
                maxLength={4096}
                onChange={(e) => setBody(e.target.value)}
                placeholder="Type your message"
                disabled={start.isPending}
              />
            </div>
          )}
        </div>

        <DialogFooter>
          <Button type="button" variant="ghost" onClick={() => close(false)}>
            Cancel
          </Button>
          <Button type="button" onClick={submit} disabled={!canSend}>
            {start.isPending ? <Spinner className="size-4" /> : null}
            {mode === "template" ? "Send template" : "Send message"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
