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
  getContactListsAction,
  getContactListByIdAction,
  createContactListAction,
  updateContactListAction,
  activateContactListAction,
  deactivateContactListAction,
  addContactsToListAction,
  removeContactFromListAction,
  importContactsToListAction,
} from "@/features/contact-lists/actions/contact-list-actions";
import { getContactsAction } from "@/features/contacts/actions/contact-actions";
import {
  useCreateContactListDialog,
  useEditContactListDialog,
  useActivateContactListDialog,
  useDeactivateContactListDialog,
} from "@/features/contact-lists/store/contact-list-store";
import type {
  CreateContactListInput,
  UpdateContactListInput,
} from "@/features/contact-lists/schema/contact-list-schema";
import type { Contact } from "@/features/contacts/types";

/** Members query key for a list — kept under contactLists.detail so it survives table-only invalidations. */
const membersKey = (listId: string) =>
  [...queryKeys.contactLists.detail(listId), "members"] as const;

// ── DataTable source ─────────────────────────────────────────────────────
export function useContactListDataTable(
  page: number,
  pageSize: number,
  search: string,
  _dateRange: { from_date: string; to_date: string },
  sortBy: string,
  sortOrder: string
) {
  return useQuery({
    queryKey: queryKeys.contactLists.list({ page, pageSize, search, sortBy, sortOrder }),
    queryFn: async () => {
      const res = await getContactListsAction({
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
(useContactListDataTable as unknown as Record<string, unknown>).isQueryHook = true;

// ── Single list (edit dialog / manage header) ────────────────────────────
export function useContactList(id: string | null) {
  return useQuery({
    queryKey: queryKeys.contactLists.detail(id ?? ""),
    queryFn: async () => {
      const res = await getContactListByIdAction(id!);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!id,
  });
}

// ── Members of a list (uses the contacts ?listId filter) ─────────────────
// Keyed under contactLists.detail so add/remove invalidation (contactLists.all) refreshes it.
export function useListMembers(listId: string | null) {
  return useQuery({
    queryKey: membersKey(listId ?? ""),
    queryFn: async () => {
      const res = await getContactsAction({ page: 1, pageSize: 100, listId: listId! });
      if (!res.success) throw new Error(res.error);
      return res.data.items;
    },
    enabled: !!listId,
  });
}

// ── Candidate contacts to add (search across all company contacts) ───────
export function useContactSearch(search: string, enabled: boolean) {
  return useQuery({
    queryKey: [...queryKeys.contacts.lists(), "picker", search] as const,
    queryFn: async () => {
      const res = await getContactsAction({ page: 1, pageSize: 20, search: search || undefined });
      if (!res.success) throw new Error(res.error);
      return res.data.items;
    },
    enabled,
  });
}

// ── Mutations ────────────────────────────────────────────────────────────
export function useCreateContactList() {
  const qc = useQueryClient();
  const { close } = useCreateContactListDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async (data: CreateContactListInput) => {
      const res = await createContactListAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.contactLists.all });
      setFieldErrors(null);
      close();
      toast.success("Contact list created successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Contact list", "create");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useUpdateContactList() {
  const qc = useQueryClient();
  const { close } = useEditContactListDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdateContactListInput }) => {
      const res = await updateContactListAction(id, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.contactLists.all });
      setFieldErrors(null);
      close();
      toast.success("Contact list updated successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Contact list", "update");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useActivateContactList() {
  const qc = useQueryClient();
  const { close } = useActivateContactListDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await activateContactListAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.contactLists.all });
      close();
      toast.success("Contact list activated successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Contact list", "activate"),
  });
}

export function useDeactivateContactList() {
  const qc = useQueryClient();
  const { close } = useDeactivateContactListDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await deactivateContactListAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.contactLists.all });
      close();
      toast.success("Contact list deactivated successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Contact list", "deactivate"),
  });
}

// Add/remove are optimistic: the members cache is mutated instantly so the dialog reacts
// with zero latency and multiple contacts can be added back-to-back. We only refresh the
// contact-lists *table* (for its contactCount badge) in the background — never the members
// query (would clobber the optimistic state / flicker) nor the search picker (must stay put).
export function useAddContactsToList() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async ({ listId, contacts }: { listId: string; contacts: Contact[] }) => {
      const res = await addContactsToListAction(
        listId,
        contacts.map((c) => c.id)
      );
      if (!res.success) throw res;
      return res.data;
    },
    onMutate: async ({ listId, contacts }) => {
      const key = membersKey(listId);
      await qc.cancelQueries({ queryKey: key });
      const previous = qc.getQueryData<Contact[]>(key);
      qc.setQueryData<Contact[]>(key, (old) => {
        const existing = new Set((old ?? []).map((m) => m.id));
        const additions = contacts.filter((c) => !existing.has(c.id));
        return [...additions, ...(old ?? [])];
      });
      return { key, previous };
    },
    onError: (error: ActionFailure, _vars, context) => {
      if (context?.previous !== undefined) qc.setQueryData(context.key, context.previous);
      handleErrorToast(error, "Contact", "update");
    },
    onSettled: () => {
      qc.invalidateQueries({ queryKey: queryKeys.contactLists.lists() });
    },
  });
}

export function useRemoveContactFromList() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async ({ listId, contactId }: { listId: string; contactId: string }) => {
      const res = await removeContactFromListAction(listId, contactId);
      if (!res.success) throw res;
    },
    onMutate: async ({ listId, contactId }) => {
      const key = membersKey(listId);
      await qc.cancelQueries({ queryKey: key });
      const previous = qc.getQueryData<Contact[]>(key);
      qc.setQueryData<Contact[]>(key, (old) => (old ?? []).filter((m) => m.id !== contactId));
      return { key, previous };
    },
    onError: (error: ActionFailure, _vars, context) => {
      if (context?.previous !== undefined) qc.setQueryData(context.key, context.previous);
      handleErrorToast(error, "Contact", "update");
    },
    onSettled: () => {
      qc.invalidateQueries({ queryKey: queryKeys.contactLists.lists() });
    },
  });
}

export function useImportContacts() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async ({ listId, file }: { listId: string; file: File }) => {
      const formData = new FormData();
      formData.append("file", file);
      const res = await importContactsToListAction(listId, formData);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.contactLists.all });
      qc.invalidateQueries({ queryKey: queryKeys.contacts.all });
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Contacts", "import"),
  });
}
