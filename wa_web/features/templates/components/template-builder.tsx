"use client";

import { useRouter } from "next/navigation";
import { ArrowLeft, RefreshCw, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import {
  useTemplate,
  useCreateTemplate,
  useUpdateTemplate,
  useRefreshTemplateStatus,
} from "@/features/templates/hooks/use-templates";
import { useDeleteTemplateDialog } from "@/features/templates/store/template-store";
import { templateToFormValues, testBuilderValues } from "@/features/templates/schema/template-schema";
import type { Template, TemplatePayload } from "@/features/templates/types";
import { TemplateBuilderForm } from "./template-builder-form";
import { TemplatePreview } from "./template-preview";
import { TemplateStatusBadge } from "./template-status-badge";
import { TemplateDialogs } from "./template-dialogs";

/** Read-only view for submitted templates (editing is rejected by the API once out of Draft). */
function ReadOnlyTemplate({ template }: { template: Template }) {
  const refresh = useRefreshTemplateStatus();
  const { open: openDelete } = useDeleteTemplateDialog();

  return (
    <div className="grid gap-6 lg:grid-cols-[1fr_360px]">
      <div className="space-y-4">
        <div className="space-y-3 rounded-xl border p-4">
          <div className="flex items-center justify-between">
            <div>
              <p className="font-medium">{template.name}</p>
              <p className="text-xs uppercase text-muted-foreground">{template.language}</p>
            </div>
            <TemplateStatusBadge status={template.status} />
          </div>

          {template.status === "Rejected" && template.rejectionReason && (
            <p className="rounded-md bg-red-500/10 p-2 text-sm text-red-600 dark:text-red-400">
              {template.rejectionReason}
            </p>
          )}

          <p className="text-xs text-muted-foreground">
            Submitted templates are read-only. To change one, delete it and create a new template —
            note that WhatsApp reserves the old name for about 30 days.
          </p>

          <div className="flex gap-2">
            {template.metaTemplateId && (
              <Button
                variant="outline"
                size="sm"
                disabled={refresh.isPending}
                onClick={() => refresh.mutate(template.id)}
              >
                {refresh.isPending ? <Spinner className="mr-2 size-4" /> : <RefreshCw className="mr-2 h-4 w-4" />}
                Refresh status
              </Button>
            )}
            <Button
              variant="outline"
              size="sm"
              className="text-destructive"
              onClick={() => openDelete(template.id, template.name)}
            >
              <Trash2 className="mr-2 h-4 w-4" />
              Delete
            </Button>
          </div>
        </div>
      </div>

      <div className="h-fit lg:sticky lg:top-6">
        <TemplatePreview values={templateToFormValues(template)} />
      </div>

      <TemplateDialogs />
    </div>
  );
}

export function TemplateBuilder({ templateId }: { templateId?: string }) {
  const router = useRouter();
  const isEdit = !!templateId;
  const { data: template, isLoading, isError } = useTemplate(templateId ?? null);
  const create = useCreateTemplate();
  const update = useUpdateTemplate();

  const onSave = async (payload: TemplatePayload) => {
    try {
      if (isEdit && templateId) await update.mutateAsync({ id: templateId, data: payload });
      else await create.mutateAsync(payload);
      router.push("/templates");
    } catch {
      /* field errors + toast surfaced by the mutation hook */
    }
  };

  const showForm = !isEdit || (!!template && template.status === "Draft");

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      <div className="rounded-2xl border bg-gradient-to-br from-primary/5 via-card to-card px-6 py-5">
        <div className="flex items-start gap-3">
          <Button variant="ghost" size="icon" className="-ml-2 mt-0.5 shrink-0" onClick={() => router.push("/templates")} aria-label="Back to templates">
            <ArrowLeft className="h-4 w-4" />
          </Button>
          <div className="flex-1">
            <div className="mb-2 flex flex-wrap items-center gap-2">
              <span className="inline-flex items-center rounded-full bg-emerald-500/15 px-2.5 py-0.5 text-xs font-medium text-emerald-600 dark:text-emerald-400">
                Marketing
              </span>
              <span className="text-xs text-muted-foreground">WhatsApp Business Template</span>
            </div>
            <h1 className="text-2xl font-bold tracking-tight">{isEdit ? "Edit template" : "New template"}</h1>
            <p className="mt-1 text-sm text-muted-foreground">
              Build a WhatsApp marketing template and submit it to Meta for approval.
            </p>
          </div>
        </div>
      </div>

      {isEdit && isLoading && (
        <div className="flex items-center justify-center py-20">
          <Spinner className="size-6" />
        </div>
      )}

      {isEdit && !isLoading && (isError || !template) && (
        <p className="text-sm text-destructive">Template not found.</p>
      )}

      {isEdit && template && template.status !== "Draft" && <ReadOnlyTemplate template={template} />}

      {showForm && (
        <TemplateBuilderForm
          key={templateId ?? "new"}
          mode={isEdit ? "edit" : "create"}
          defaultValues={isEdit && template ? templateToFormValues(template) : testBuilderValues()}
          onSave={onSave}
          isSaving={create.isPending || update.isPending}
          fieldErrors={isEdit ? update.fieldErrors : create.fieldErrors}
        />
      )}
    </div>
  );
}
