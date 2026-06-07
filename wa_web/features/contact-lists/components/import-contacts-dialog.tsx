"use client";

import { useState } from "react";
import { Upload, FileSpreadsheet } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { useImportContactsDialog } from "@/features/contact-lists/store/contact-list-store";
import { useContactList, useImportContacts } from "@/features/contact-lists/hooks/use-contact-lists";
import type { ImportContactsResult } from "@/features/contact-lists/types";

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-md border bg-muted/30 p-2 text-center">
      <div className="text-lg font-semibold">{value}</div>
      <div className="text-xs text-muted-foreground">{label}</div>
    </div>
  );
}

export function ImportContactsDialog() {
  const { isOpen, selectedId, close } = useImportContactsDialog();
  const { data: list } = useContactList(isOpen ? selectedId : null);
  const importMut = useImportContacts();
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<ImportContactsResult | null>(null);

  const reset = () => {
    setFile(null);
    setResult(null);
  };

  const onSubmit = () => {
    if (!selectedId || !file) return;
    importMut.mutate(
      { listId: selectedId, file },
      { onSuccess: (data) => setResult(data) }
    );
  };

  return (
    <Dialog
      open={isOpen}
      onOpenChange={(open) => {
        if (!open) {
          close();
          reset();
        }
      }}
    >
      <DialogContent className="sm:max-w-md max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Import contacts</DialogTitle>
          <DialogDescription>
            Upload an <b>.xlsx</b> with <b>Phone</b> and <b>Name</b> columns
            {list ? ` into “${list.name}”` : ""}. Existing numbers are updated, not duplicated.
          </DialogDescription>
        </DialogHeader>

        {result ? (
          <div className="space-y-3">
            <div className="grid grid-cols-3 gap-2">
              <Stat label="New" value={result.imported} />
              <Stat label="Updated" value={result.updated} />
              <Stat label="Added to list" value={result.addedToList} />
            </div>
            {result.skipped > 0 ? (
              <div className="space-y-1">
                <p className="text-sm font-medium text-destructive">
                  {result.skipped} of {result.totalRows} rows skipped
                </p>
                <div className="max-h-40 divide-y overflow-y-auto rounded-md border">
                  {result.skippedRows.map((s) => (
                    <div
                      key={s.row}
                      className="flex items-center justify-between gap-2 px-3 py-1.5 text-xs"
                    >
                      <span className="font-mono">
                        Row {s.row}
                        {s.phone ? ` · ${s.phone}` : ""}
                      </span>
                      <span className="text-muted-foreground">{s.reason}</span>
                    </div>
                  ))}
                </div>
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">
                All {result.totalRows} rows imported cleanly.
              </p>
            )}
            <Button className="w-full" variant="secondary" onClick={reset}>
              Import another file
            </Button>
          </div>
        ) : (
          <div className="space-y-4">
            <label className="flex cursor-pointer flex-col items-center justify-center gap-2 rounded-lg border border-dashed py-8 text-center hover:bg-muted/40">
              <FileSpreadsheet className="h-6 w-6 text-muted-foreground" />
              <span className="text-sm">
                {file ? file.name : "Click to choose an .xlsx file"}
              </span>
              <input
                type="file"
                accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                className="hidden"
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              />
            </label>
            <Button className="w-full" disabled={!file || importMut.isPending} onClick={onSubmit}>
              {importMut.isPending ? (
                <>
                  <Spinner className="mr-2 size-4" />
                  Importing…
                </>
              ) : (
                <>
                  <Upload className="mr-2 h-4 w-4" />
                  Import
                </>
              )}
            </Button>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
