"use client";

import { useEffect, useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";
import { queryKeys } from "@/lib/hooks/query-keys";
import { getTemplatesAction } from "@/features/templates/actions/template-actions";
import { getContactListsAction } from "@/features/contact-lists/actions/contact-list-actions";
import { getContactsAction } from "@/features/contacts/actions/contact-actions";
import {
  createCampaignSchema,
  updateCampaignSchema,
  type CreateCampaignInput,
} from "@/features/campaigns/schema/campaign-schema";
import type { Campaign } from "@/features/campaigns/types";

interface CampaignFormProps {
  mode: "create" | "edit";
  defaultValues?: Campaign;
  onSubmit: (data: CreateCampaignInput) => void;
  isLoading: boolean;
  fieldErrors?: Record<string, string> | null;
}

type TargetMode = "lists" | "contacts";

export function CampaignForm({
  mode,
  defaultValues,
  onSubmit,
  isLoading,
  fieldErrors,
}: CampaignFormProps) {
  const [targetMode, setTargetMode] = useState<TargetMode>(
    defaultValues?.contactIds && defaultValues.contactIds.length > 0 ? "contacts" : "lists"
  );

  const {
    register,
    handleSubmit,
    control,
    watch,
    setValue,
    setError,
    formState: { errors },
  } = useForm<CreateCampaignInput>({
    resolver: zodResolver(mode === "create" ? createCampaignSchema : updateCampaignSchema),
    defaultValues: {
      name: defaultValues?.name ?? "",
      templateId: defaultValues?.templateId ?? "",
      contactListIds: defaultValues?.contactListIds ?? [],
      contactIds: defaultValues?.contactIds ?? [],
      variableMapping: defaultValues?.variableMapping ?? "{}",
      scheduleType: defaultValues?.scheduleType ?? "Immediate",
      scheduledAt: defaultValues?.scheduledAt ?? null,
      recurrenceCron: defaultValues?.recurrenceCron ?? null,
    },
  });

  const scheduleType = watch("scheduleType");
  const selectedListIds = watch("contactListIds") ?? [];
  const selectedContactIds = watch("contactIds") ?? [];

  // Load approved templates for the picker
  const { data: templatesData } = useQuery({
    queryKey: queryKeys.templates.list({ page: 1, pageSize: 50, status: "Approved" }),
    queryFn: async () => {
      const res = await getTemplatesAction({ page: 1, pageSize: 50, status: "Approved" });
      if (!res.success) throw new Error(res.error);
      return res.data.items;
    },
  });

  // Load contact lists
  const { data: listsData } = useQuery({
    queryKey: queryKeys.contactLists.list({ page: 1, pageSize: 100 }),
    queryFn: async () => {
      const res = await getContactListsAction({ page: 1, pageSize: 100 });
      if (!res.success) throw new Error(res.error);
      return res.data.items;
    },
  });

  // Load contacts (for individual selection)
  const [contactSearch, setContactSearch] = useState("");
  const { data: contactsData } = useQuery({
    queryKey: queryKeys.contacts.list({ page: 1, pageSize: 30, search: contactSearch }),
    queryFn: async () => {
      const res = await getContactsAction({
        page: 1,
        pageSize: 30,
        search: contactSearch || undefined,
      });
      if (!res.success) throw new Error(res.error);
      return res.data.items;
    },
    enabled: targetMode === "contacts",
  });

  useEffect(() => {
    if (fieldErrors) {
      for (const [field, message] of Object.entries(fieldErrors)) {
        setError(field as keyof CreateCampaignInput, { message });
      }
    }
  }, [fieldErrors, setError]);

  // When switching target mode, clear the other side
  const handleTargetModeChange = (mode: TargetMode) => {
    setTargetMode(mode);
    if (mode === "lists") setValue("contactIds", []);
    else setValue("contactListIds", []);
  };

  const toggleList = (id: string) => {
    const current = selectedListIds;
    const next = current.includes(id)
      ? current.filter((x) => x !== id)
      : [...current, id];
    setValue("contactListIds", next);
  };

  const toggleContact = (id: string) => {
    const current = selectedContactIds;
    const next = current.includes(id)
      ? current.filter((x) => x !== id)
      : [...current, id];
    setValue("contactIds", next);
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
      {/* Name */}
      <div className="space-y-2">
        <Label htmlFor="name">Campaign name</Label>
        <Input id="name" placeholder="March promo blast" {...register("name")} />
        {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
      </div>

      {/* Template */}
      <div className="space-y-2">
        <Label>Template</Label>
        <Controller
          control={control}
          name="templateId"
          render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger>
                <SelectValue placeholder="Select an approved template" />
              </SelectTrigger>
              <SelectContent>
                {templatesData?.map((t) => (
                  <SelectItem key={t.id} value={t.id}>
                    {t.name} — {t.language}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
        {errors.templateId && (
          <p className="text-xs text-destructive">{errors.templateId.message}</p>
        )}
      </div>

      {/* Target mode toggle */}
      <div className="space-y-3">
        <Label>Send to</Label>
        <div className="flex gap-2">
          <Button
            type="button"
            size="sm"
            variant={targetMode === "lists" ? "default" : "outline"}
            onClick={() => handleTargetModeChange("lists")}
          >
            Contact lists
          </Button>
          <Button
            type="button"
            size="sm"
            variant={targetMode === "contacts" ? "default" : "outline"}
            onClick={() => handleTargetModeChange("contacts")}
          >
            Individual contacts
          </Button>
        </div>

        {/* List multi-select */}
        {targetMode === "lists" && (
          <div className="space-y-2">
            <div className="rounded-md border max-h-48 overflow-y-auto divide-y">
              {listsData?.length === 0 && (
                <p className="p-3 text-sm text-muted-foreground">No contact lists found.</p>
              )}
              {listsData?.map((list) => (
                <label
                  key={list.id}
                  className="flex items-center gap-3 px-3 py-2 cursor-pointer hover:bg-muted/40"
                >
                  <Checkbox
                    checked={selectedListIds.includes(list.id)}
                    onCheckedChange={() => toggleList(list.id)}
                  />
                  <div className="min-w-0 flex-1">
                    <div className="text-sm font-medium truncate">{list.name}</div>
                    {list.description && (
                      <div className="text-xs text-muted-foreground truncate">{list.description}</div>
                    )}
                  </div>
                  <Badge variant="secondary" className="shrink-0 text-xs">
                    {list.contactCount}
                  </Badge>
                </label>
              ))}
            </div>
            {selectedListIds.length > 0 && (
              <p className="text-xs text-muted-foreground">
                {selectedListIds.length} list{selectedListIds.length !== 1 ? "s" : ""} selected
              </p>
            )}
            {errors.contactListIds && (
              <p className="text-xs text-destructive">{errors.contactListIds.message}</p>
            )}
          </div>
        )}

        {/* Individual contacts multi-select */}
        {targetMode === "contacts" && (
          <div className="space-y-2">
            <Input
              placeholder="Search contacts..."
              value={contactSearch}
              onChange={(e) => setContactSearch(e.target.value)}
              className="h-8 text-sm"
            />
            <div className="rounded-md border max-h-48 overflow-y-auto divide-y">
              {contactsData?.length === 0 && (
                <p className="p-3 text-sm text-muted-foreground">No contacts found.</p>
              )}
              {contactsData?.map((contact) => (
                <label
                  key={contact.id}
                  className="flex items-center gap-3 px-3 py-2 cursor-pointer hover:bg-muted/40"
                >
                  <Checkbox
                    checked={selectedContactIds.includes(contact.id)}
                    onCheckedChange={() => toggleContact(contact.id)}
                  />
                  <div className="min-w-0 flex-1">
                    <div className="text-sm font-medium truncate">{contact.name}</div>
                    <div className="text-xs text-muted-foreground">{contact.phone}</div>
                  </div>
                </label>
              ))}
            </div>
            {selectedContactIds.length > 0 && (
              <p className="text-xs text-muted-foreground">
                {selectedContactIds.length} contact{selectedContactIds.length !== 1 ? "s" : ""}{" "}
                selected
              </p>
            )}
            {errors.contactIds && (
              <p className="text-xs text-destructive">{errors.contactIds.message}</p>
            )}
          </div>
        )}
      </div>

      {/* Schedule type */}
      <div className="space-y-2">
        <Label>Schedule</Label>
        <Controller
          control={control}
          name="scheduleType"
          render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Immediate">Send immediately on launch</SelectItem>
                <SelectItem value="OneTime">Schedule for a specific date/time</SelectItem>
                <SelectItem value="Recurring">Recurring (cron)</SelectItem>
              </SelectContent>
            </Select>
          )}
        />

        {scheduleType === "OneTime" && (
          <div className="space-y-1">
            <Label htmlFor="scheduledAt" className="text-xs text-muted-foreground">
              Send date and time (UTC)
            </Label>
            <Input
              id="scheduledAt"
              type="datetime-local"
              {...register("scheduledAt")}
            />
            {errors.scheduledAt && (
              <p className="text-xs text-destructive">{errors.scheduledAt.message}</p>
            )}
          </div>
        )}

        {scheduleType === "Recurring" && (
          <div className="space-y-1">
            <Label htmlFor="recurrenceCron" className="text-xs text-muted-foreground">
              Cron expression (e.g. <code>0 9 * * 1</code> = every Monday at 9am UTC)
            </Label>
            <Input
              id="recurrenceCron"
              placeholder="0 9 * * 1"
              {...register("recurrenceCron")}
            />
            {errors.recurrenceCron && (
              <p className="text-xs text-destructive">{errors.recurrenceCron.message}</p>
            )}
          </div>
        )}
      </div>

      <Button type="submit" className="w-full" disabled={isLoading}>
        {isLoading ? (
          <>
            <Spinner className="mr-2 size-4" />
            {mode === "create" ? "Creating…" : "Updating…"}
          </>
        ) : mode === "create" ? (
          "Create Campaign"
        ) : (
          "Update Campaign"
        )}
      </Button>
    </form>
  );
}
