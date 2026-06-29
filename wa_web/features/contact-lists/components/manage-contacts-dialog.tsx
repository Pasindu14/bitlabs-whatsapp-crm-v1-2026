"use client";

import { useState, useEffect } from "react";
import { Plus, X, Search, Users, UserCheck } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Spinner } from "@/components/ui/spinner";
import { useManageContactsDialog } from "@/features/contact-lists/store/contact-list-store";
import {
  useContactList,
  useListMembers,
  useContactSearch,
  useAddContactsToList,
  useRemoveContactFromList,
} from "@/features/contact-lists/hooks/use-contact-lists";

function ContactAvatar({ name }: { name: string }) {
  const initials = name
    .split(" ")
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? "")
    .join("");
  return (
    <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-primary/10 text-xs font-semibold text-primary">
      {initials || "?"}
    </div>
  );
}

export function ManageContactsDialog() {
  const { isOpen, selectedId, close } = useManageContactsDialog();
  const { data: list } = useContactList(isOpen ? selectedId : null);
  const { data: members, isLoading: membersLoading } = useListMembers(isOpen ? selectedId : null);

  const [inputValue, setInputValue] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(inputValue), 300);
    return () => clearTimeout(timer);
  }, [inputValue]);

  const { data: candidates, isFetching: searching } = useContactSearch(debouncedSearch, isOpen);
  const add = useAddContactsToList();
  const remove = useRemoveContactFromList();

  const memberIds = new Set((members ?? []).map((m) => m.id));

  function handleOpenChange(open: boolean) {
    if (!open) {
      close();
      setInputValue("");
      setDebouncedSearch("");
    }
  }

  return (
    <Dialog open={isOpen} onOpenChange={handleOpenChange}>
      <DialogContent className="flex max-h-[85vh] flex-col gap-0 overflow-hidden p-0 sm:max-w-2xl">
        {/* Header */}
        <DialogHeader className="border-b px-6 py-5">
          <DialogTitle className="text-lg">Manage contacts</DialogTitle>
          <DialogDescription className="text-sm">
            {list ? `Add or remove contacts in "${list.name}".` : "Add or remove contacts in this list."}
          </DialogDescription>
        </DialogHeader>

        <div className="flex min-h-0 flex-1 flex-col gap-0 overflow-hidden">
          {/* Search section */}
          <div className="border-b px-6 py-4">
            <div className="mb-3 flex items-center gap-2">
              <Search className="h-4 w-4 text-muted-foreground" />
              <span className="text-sm font-medium text-foreground">Add contacts</span>
              {searching && <Spinner className="size-3.5" />}
            </div>
            <div className="relative">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                className="pl-9 pr-4"
                placeholder="Search contacts to add..."
                value={inputValue}
                onChange={(e) => setInputValue(e.target.value)}
                autoFocus
              />
            </div>

            <div className="mt-2 max-h-52 divide-y overflow-y-auto rounded-lg border bg-background">
              {searching ? (
                <div className="flex items-center justify-center py-8">
                  <Spinner className="size-5" />
                </div>
              ) : (candidates ?? []).length === 0 ? (
                <div className="flex flex-col items-center justify-center gap-1.5 py-8">
                  <Users className="h-5 w-5 text-muted-foreground/50" />
                  <p className="text-sm text-muted-foreground">
                    {debouncedSearch ? "No contacts match your search." : "Start typing to search contacts."}
                  </p>
                </div>
              ) : (
                (candidates ?? []).map((c) => {
                  const inList = memberIds.has(c.id);
                  return (
                    <div
                      key={c.id}
                      className="flex items-center gap-3 px-4 py-2.5 transition-colors hover:bg-muted/40"
                    >
                      <ContactAvatar name={c.name} />
                      <div className="min-w-0 flex-1">
                        <div className="truncate text-sm font-medium leading-tight">{c.name}</div>
                        <div className="truncate font-mono text-xs text-muted-foreground">{c.phone}</div>
                      </div>
                      <Button
                        size="sm"
                        variant={inList ? "ghost" : "secondary"}
                        disabled={inList}
                        className="shrink-0"
                        onClick={() => selectedId && add.mutate({ listId: selectedId, contacts: [c] })}
                      >
                        {inList ? (
                          <>
                            <UserCheck className="mr-1.5 h-3.5 w-3.5 text-green-600" />
                            <span className="text-green-600">Added</span>
                          </>
                        ) : (
                          <>
                            <Plus className="mr-1.5 h-3.5 w-3.5" />
                            Add
                          </>
                        )}
                      </Button>
                    </div>
                  );
                })
              )}
            </div>
          </div>

          {/* Members section */}
          <div className="flex min-h-0 flex-col px-6 py-4">
            <div className="mb-3 flex items-center gap-2">
              <Users className="h-4 w-4 text-muted-foreground" />
              <span className="text-sm font-medium text-foreground">In this list</span>
              <Badge variant="secondary" className="px-2 py-0 text-xs">
                {members?.length ?? 0}
              </Badge>
            </div>

            <div className="min-h-0 flex-1 overflow-y-auto rounded-lg border bg-background">
              {membersLoading ? (
                <div className="flex items-center justify-center py-8">
                  <Spinner className="size-5" />
                </div>
              ) : (members ?? []).length === 0 ? (
                <div className="flex flex-col items-center justify-center gap-1.5 py-8">
                  <Users className="h-5 w-5 text-muted-foreground/50" />
                  <p className="text-sm text-muted-foreground">No contacts in this list yet.</p>
                </div>
              ) : (
                <div className="divide-y">
                  {(members ?? []).map((m) => (
                    <div
                      key={m.id}
                      className="flex items-center gap-3 px-4 py-2.5 transition-colors hover:bg-muted/40"
                    >
                      <ContactAvatar name={m.name} />
                      <div className="min-w-0 flex-1">
                        <div className="truncate text-sm font-medium leading-tight">{m.name}</div>
                        <div className="truncate font-mono text-xs text-muted-foreground">{m.phone}</div>
                      </div>
                      <Button
                        size="icon"
                        variant="ghost"
                        className="h-8 w-8 shrink-0 text-muted-foreground hover:bg-destructive/10 hover:text-destructive"
                        onClick={() => selectedId && remove.mutate({ listId: selectedId, contactId: m.id })}
                      >
                        <X className="h-4 w-4" />
                        <span className="sr-only">Remove from list</span>
                      </Button>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
