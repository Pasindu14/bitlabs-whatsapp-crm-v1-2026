"use client";

import { useState } from "react";
import {
  useMutation,
  useQuery,
  useQueryClient,
  keepPreviousData,
} from "@tanstack/react-query";
import { toast } from "sonner";
import { queryKeys } from "@/lib/hooks/query-keys";
import { handleErrorToast } from "@/lib/hooks/use-error-toast";
import type { ActionFailure } from "@/lib/types/actions";
import {
  getContactsAction,
  getContactByIdAction,
  createContactAction,
  importContactsAction,
  updateContactAction,
  activateContactAction,
  deactivateContactAction,
} from "@/features/contacts/actions/contact-actions";
import {
  useCreateContactDialog,
  useEditContactDialog,
  useActivateContactDialog,
  useDeactivateContactDialog,
} from "@/features/contacts/store/contact-store";
import type {
  CreateContactInput,
  UpdateContactInput,
} from "@/features/contacts/schema/contact-schema";
import type { Contact, ImportContactsInput } from "@/features/contacts/types";

// ── DataTable source ─────────────────────────────────────────────────────
// A useQuery hook (NOT a plain fn) so mutations can invalidate contacts.all and the
// table refetches automatically — no manual refresh.
export function useContactDataTable(
  page: number,
  pageSize: number,
  search: string,
  _dateRange: { from_date: string; to_date: string },
  sortBy: string,
  sortOrder: string
) {
  return useQuery({
    queryKey: queryKeys.contacts.list({ page, pageSize, search, sortBy, sortOrder }),
    queryFn: async () => {
      const res = await getContactsAction({
        page,
        pageSize,
        search: search || undefined,
        sortBy: sortBy || undefined,
        sortOrder: (sortOrder as "asc" | "desc") || undefined,
      });
      if (!res.success) throw new Error(res.error);

      const { items, pagination } = res.data;
      return {
        success: true as const,
        data: items,
        pagination: {
          page: pagination.page,
          limit: pagination.pageSize,
          total_pages: pagination.totalPages,
          total_items: pagination.total,
        },
      };
    },
    placeholderData: keepPreviousData,
  });
}
// Mark as a query hook so the DataTable calls it as a hook (enables auto-refresh).
(useContactDataTable as unknown as Record<string, unknown>).isQueryHook = true;

// ── Single contact (edit dialog) ─────────────────────────────────────────
export function useContact(id: string | null) {
  return useQuery({
    queryKey: queryKeys.contacts.detail(id ?? ""),
    queryFn: async () => {
      const res = await getContactByIdAction(id!);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!id,
  });
}

// ── Mutations ────────────────────────────────────────────────────────────
export function useCreateContact() {
  const qc = useQueryClient();
  const { close } = useCreateContactDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async (data: CreateContactInput) => {
      const res = await createContactAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.contacts.all });
      setFieldErrors(null);
      close();
      toast.success("Contact added successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Contact", "create");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useImportContacts() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (data: ImportContactsInput) => {
      const res = await importContactsAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: (result) => {
      qc.invalidateQueries({ queryKey: queryKeys.contacts.all });
      if (result.imported > 0)
        toast.success(`Imported ${result.imported} contact${result.imported === 1 ? "" : "s"}`);
      else toast.info("No new contacts were imported");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Contact", "import"),
  });
}

export function useUpdateContact() {
  const qc = useQueryClient();
  const { close } = useEditContactDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdateContactInput }) => {
      const res = await updateContactAction(id, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.contacts.all });
      setFieldErrors(null);
      close();
      toast.success("Contact updated successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Contact", "update");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useActivateContact() {
  const qc = useQueryClient();
  const { close } = useActivateContactDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await activateContactAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.contacts.all });
      close();
      toast.success("Contact activated successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Contact", "activate"),
  });
}

export function useDeactivateContact() {
  const qc = useQueryClient();
  const { close } = useDeactivateContactDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await deactivateContactAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.contacts.all });
      close();
      toast.success("Contact deactivated successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Contact", "deactivate"),
  });
}

// ── Opt-out override (row quick-action) ──────────────────────────────────
// Company-admin one-click suppress / re-enable, the sanctioned way to lift a STOP. Reuses the update
// endpoint (which requires name+phone) by echoing the row's current values alongside the flipped flag.
export function useSetContactOptOut() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async ({ contact, isOptedOut }: { contact: Contact; isOptedOut: boolean }) => {
      const res = await updateContactAction(contact.id, {
        name: contact.name,
        phone: contact.phone,
        isOptedOut,
      });
      if (!res.success) throw res;
      return isOptedOut;
    },
    onSuccess: (isOptedOut) => {
      qc.invalidateQueries({ queryKey: queryKeys.contacts.all });
      toast.success(
        isOptedOut
          ? "Contact opted out — sending suppressed"
          : "Sending re-enabled for this contact"
      );
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Contact", "update"),
  });
}
