"use client";

import { useState } from "react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Checkbox } from "@/components/ui/checkbox";
import { Spinner } from "@/components/ui/spinner";
import { useImportContactsDialog } from "@/features/contacts/store/contact-store";
import { useImportContacts } from "@/features/contacts/hooks/use-contacts";
import type { ImportContactRow, ImportContactsResult } from "@/features/contacts/types";

/** Parses pasted lines into rows. Each line is "phone" or "phone, Name" (comma or tab separated). */
function parseRows(text: string): ImportContactRow[] {
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      const [phone, ...rest] = line.split(/[,\t]/);
      const name = rest.join(",").trim();
      return { phone: phone.trim(), name: name || undefined };
    })
    .filter((r) => r.phone.length > 0);
}

export function ContactImportDialog() {
  const { isOpen, close } = useImportContactsDialog();
  const { mutate, isPending } = useImportContacts();
  const [text, setText] = useState("");
  const [markOptedIn, setMarkOptedIn] = useState(false);
  const [result, setResult] = useState<ImportContactsResult | null>(null);

  const rows = parseRows(text);

  function reset() {
    setText("");
    setMarkOptedIn(false);
    setResult(null);
  }

  function handleClose() {
    close();
    reset();
  }

  function submit() {
    if (rows.length === 0) return;
    mutate(
      { contacts: rows, markOptedIn },
      { onSuccess: (r) => setResult(r) }
    );
  }

  return (
    <Dialog open={isOpen} onOpenChange={(open) => !open && handleClose()}>
      <DialogContent className="sm:max-w-lg max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Import contacts</DialogTitle>
          <DialogDescription>
            Paste one number per line, in international format with country code. Optionally add a
            name after a comma. Duplicates and invalid numbers are skipped automatically.
          </DialogDescription>
        </DialogHeader>

        {result ? (
          <div className="space-y-4">
            <div className="grid grid-cols-2 gap-3">
              {(
                [
                  ["Imported", result.imported],
                  ["Already existed", result.duplicateExisting],
                  ["Duplicate in list", result.duplicateInFile],
                  ["Invalid", result.invalid],
                ] as [string, number][]
              ).map(([label, count]) => (
                <div key={label} className="rounded-lg border p-3">
                  <div className="text-xs text-muted-foreground">{label}</div>
                  <div className="mt-1 text-2xl font-bold">{count.toLocaleString()}</div>
                </div>
              ))}
            </div>

            {result.skipped.length > 0 && (
              <div className="rounded-lg border">
                <div className="border-b px-3 py-2 text-xs font-medium text-muted-foreground">
                  Skipped ({result.skipped.length})
                </div>
                <div className="max-h-40 overflow-y-auto divide-y text-sm">
                  {result.skipped.slice(0, 100).map((s, i) => (
                    <div key={i} className="flex items-center justify-between gap-3 px-3 py-1.5">
                      <span className="font-mono text-xs">{s.phone}</span>
                      <span className="text-xs text-muted-foreground">{s.reason}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}

            <div className="flex justify-end gap-2">
              <Button variant="outline" onClick={reset}>
                Import more
              </Button>
              <Button onClick={handleClose}>Done</Button>
            </div>
          </div>
        ) : (
          <div className="space-y-4">
            <Textarea
              value={text}
              onChange={(e) => setText(e.target.value)}
              placeholder={"971501234567\n971509876543, Jane Doe\n447700900123, John"}
              rows={8}
              className="font-mono text-sm"
            />

            <label className="flex items-start gap-3 rounded-md border p-3 cursor-pointer">
              <Checkbox
                className="mt-0.5"
                checked={markOptedIn}
                onCheckedChange={(c) => setMarkOptedIn(c === true)}
              />
              <div className="space-y-1">
                <div className="text-sm font-medium">Mark all as opted-in</div>
                <p className="text-xs text-muted-foreground">
                  Only enable if you have proof of consent on file for this list. Campaigns skip
                  contacts without opt-in unless the consent override is on.
                </p>
              </div>
            </label>

            <div className="flex items-center justify-between">
              <span className="text-xs text-muted-foreground">
                {rows.length > 0 ? `${rows.length} number${rows.length === 1 ? "" : "s"} detected` : " "}
              </span>
              <Button onClick={submit} disabled={isPending || rows.length === 0}>
                {isPending && <Spinner className="mr-2 size-4" />}
                Import {rows.length > 0 ? rows.length : ""}
              </Button>
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
