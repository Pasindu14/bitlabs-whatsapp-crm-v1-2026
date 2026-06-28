"use client";

import { useEffect } from "react";
import { useForm, useFieldArray, useWatch, Controller, type Resolver } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Braces, Save } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { useActiveWabaConnections } from "@/features/messages/hooks/use-messages";
import {
  builderSchema,
  buildTemplatePayload,
  placeholderIndices,
  type BuilderFormValues,
} from "@/features/templates/schema/template-schema";
import { HEADER_TYPES, type TemplatePayload } from "@/features/templates/types";
import { MediaUpload } from "./media-upload";
import { ButtonsEditor } from "./buttons-editor";
import { TemplatePreview } from "./template-preview";

const LANGUAGES = [
  { code: "en_US", label: "English (US)" },
  { code: "en_GB", label: "English (UK)" },
  { code: "es_ES", label: "Spanish (Spain)" },
  { code: "es_MX", label: "Spanish (Mexico)" },
  { code: "pt_BR", label: "Portuguese (Brazil)" },
  { code: "fr", label: "French" },
  { code: "de", label: "German" },
  { code: "ar", label: "Arabic" },
  { code: "hi", label: "Hindi" },
  { code: "id", label: "Indonesian" },
  { code: "si_LK", label: "Sinhala" },
  { code: "ta", label: "Tamil" },
];

const HEADER_LABELS: Record<(typeof HEADER_TYPES)[number], string> = {
  none: "None",
  text: "Text",
  image: "Image",
  document: "Document",
};

const SectionCard = ({ title, hint, step, children }: { title: string; hint?: string; step: number; children: React.ReactNode }) => (
  <section className="relative overflow-hidden rounded-xl border bg-card shadow-sm transition-shadow hover:shadow-md">
    <div className="absolute inset-y-0 left-0 w-[3px] bg-primary" />
    <div className="space-y-4 py-5 pl-6 pr-5">
      <div className="flex items-center gap-3">
        <div className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-primary/10 text-[11px] font-bold text-primary">
          {step}
        </div>
        <div>
          <h3 className="text-sm font-semibold leading-none">{title}</h3>
          {hint && <p className="mt-1 text-xs text-muted-foreground">{hint}</p>}
        </div>
      </div>
      {children}
    </div>
  </section>
);

interface TemplateBuilderFormProps {
  mode: "create" | "edit";
  defaultValues: BuilderFormValues;
  onSave: (payload: TemplatePayload) => void;
  isSaving: boolean;
  fieldErrors?: Record<string, string> | null;
}

export function TemplateBuilderForm({ mode, defaultValues, onSave, isSaving, fieldErrors }: TemplateBuilderFormProps) {
  const { data: connections, isLoading: loadingConnections, isError: connectionsError } = useActiveWabaConnections();

  const form = useForm<BuilderFormValues>({
    resolver: zodResolver(builderSchema) as Resolver<BuilderFormValues>,
    defaultValues,
  });
  const {
    control,
    register,
    handleSubmit,
    setValue,
    getValues,
    setError,
    formState: { errors },
  } = form;

  const { fields: exampleFields, append: appendExample, remove: removeExample } = useFieldArray({
    control,
    name: "bodyExamples",
  });

  // useWatch (not watch()) so the live preview stays React-Compiler-safe. defaultValue supplies
  // every field and all fields are registered, so the runtime value is always complete.
  const values = useWatch({ control, defaultValue: defaultValues }) as BuilderFormValues;
  const body = values.body;
  const headerType = values.headerType;

  // Keep the body-example inputs in sync with the number of {{n}} placeholders in the body.
  useEffect(() => {
    const needed = placeholderIndices(body || "").length;
    const current = getValues("bodyExamples").length;
    if (needed > current) {
      for (let i = current; i < needed; i++) appendExample({ value: "" });
    } else if (needed < current) {
      for (let i = current - 1; i >= needed; i--) removeExample(i);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [body]);

  // Bind API field errors back onto inputs (mostly `name`; nested paths are caught client-side).
  useEffect(() => {
    if (fieldErrors) {
      for (const [field, message] of Object.entries(fieldErrors)) {
        setError(field as keyof BuilderFormValues, { message });
      }
    }
  }, [fieldErrors, setError]);

  // ── TESTING ONLY ──────────────────────────────────────────────────────────
  // Seed the create form so a valid draft saves in one click: give it a unique name
  // (avoids (name, language) collisions on repeat) and auto-pick the first active
  // WhatsApp number. Remove this block (and revert to emptyBuilderValues) before prod.
  useEffect(() => {
    if (mode === "create") {
      setValue("name", `test_template_${Date.now().toString(36)}`, { shouldDirty: true });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (mode === "create" && connections?.length && !getValues("wabaConnectionId")) {
      setValue("wabaConnectionId", connections[0].id, { shouldValidate: true, shouldDirty: true });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [connections, mode]);

  const insertVariable = () => {
    const next = placeholderIndices(getValues("body")).length + 1;
    const current = getValues("body");
    setValue("body", `${current}${current && !current.endsWith(" ") ? " " : ""}{{${next}}}`, {
      shouldValidate: false,
      shouldDirty: true,
    });
  };

  const submit = handleSubmit((v) => onSave(buildTemplatePayload(v)));

  return (
    <form onSubmit={submit}>
      <div className="grid gap-6 lg:grid-cols-[1fr_360px]">
        {/* ── Form ─────────────────────────────────────────────────── */}
        <div className="space-y-6">
          <SectionCard title="Basics" step={1}>
            <div className="flex items-center gap-2">
              <span className="text-xs text-muted-foreground">Category is fixed to Marketing in this version.</span>
            </div>

            <div className="space-y-2">
              <Label htmlFor="wabaConnectionId">WhatsApp number</Label>
              <Controller
                control={control}
                name="wabaConnectionId"
                render={({ field }) => (
                  <Select onValueChange={field.onChange} value={field.value} disabled={isSaving}>
                    <SelectTrigger id="wabaConnectionId">
                      <SelectValue
                        placeholder={
                          loadingConnections
                            ? "Loading connections…"
                            : connectionsError
                              ? "Failed to load connections"
                              : "Select a phone number"
                        }
                      />
                    </SelectTrigger>
                    <SelectContent>
                      {connections?.length === 0 && (
                        <SelectItem value="_none" disabled>
                          No active connections found
                        </SelectItem>
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
              {errors.wabaConnectionId && (
                <p className="text-xs text-destructive">{errors.wabaConnectionId.message}</p>
              )}
            </div>

            <div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor="name">Template name</Label>
                <Input id="name" placeholder="order_confirmation" disabled={isSaving} {...register("name")} />
                {errors.name ? (
                  <p className="text-xs text-destructive">{errors.name.message}</p>
                ) : (
                  <p className="text-xs text-muted-foreground">Lowercase, numbers and underscores only.</p>
                )}
              </div>
              <div className="space-y-2">
                <Label htmlFor="language">Language</Label>
                <Controller
                  control={control}
                  name="language"
                  render={({ field }) => (
                    <Select onValueChange={field.onChange} value={field.value} disabled={isSaving}>
                      <SelectTrigger id="language">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {LANGUAGES.map((l) => (
                          <SelectItem key={l.code} value={l.code}>
                            {l.label}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  )}
                />
                {errors.language && <p className="text-xs text-destructive">{errors.language.message}</p>}
              </div>
            </div>
          </SectionCard>

          <SectionCard title="Header" hint="Optional. A title or media shown above the message." step={2}>
            <Controller
              control={control}
              name="headerType"
              render={({ field }) => (
                <Select onValueChange={field.onChange} value={field.value} disabled={isSaving}>
                  <SelectTrigger className="w-48">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {HEADER_TYPES.map((t) => (
                      <SelectItem key={t} value={t}>
                        {HEADER_LABELS[t]}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />

            {headerType === "text" && (
              <div className="space-y-2">
                <Input placeholder="e.g. Order {{1}} confirmed" disabled={isSaving} {...register("headerText")} />
                {errors.headerText && <p className="text-xs text-destructive">{errors.headerText.message}</p>}
                {/\{\{\d+\}\}/.test(values.headerText) && (
                  <div className="space-y-1">
                    <Label className="text-xs">Example for {"{{1}}"}</Label>
                    <Input placeholder="Sample value" disabled={isSaving} {...register("headerTextExample")} />
                    {errors.headerTextExample && (
                      <p className="text-xs text-destructive">{errors.headerTextExample.message}</p>
                    )}
                  </div>
                )}
              </div>
            )}

            {(headerType === "image" || headerType === "document") && (
              <div className="space-y-1">
                <MediaUpload
                  mediaType={headerType}
                  value={values.headerMediaHandle}
                  onChange={(h, previewId) => {
                    setValue("headerMediaHandle", h, { shouldValidate: true, shouldDirty: true });
                    setValue("headerMediaPreviewId", previewId, { shouldDirty: true });
                  }}
                  disabled={isSaving}
                />
                {errors.headerMediaHandle && (
                  <p className="text-xs text-destructive">{errors.headerMediaHandle.message}</p>
                )}
              </div>
            )}
          </SectionCard>

          <SectionCard title="Body" hint="The main message. Use variables for personalization." step={3}>
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <Label htmlFor="body">Message text</Label>
                <Button type="button" variant="outline" size="sm" onClick={insertVariable} disabled={isSaving}>
                  <Braces className="mr-1 h-3.5 w-3.5" />
                  Add variable
                </Button>
              </div>
              <Textarea id="body" rows={5} placeholder="Hi {{1}}, your order is confirmed!" disabled={isSaving} {...register("body")} />
              {errors.body && <p className="text-xs text-destructive">{errors.body.message}</p>}
            </div>

            {exampleFields.length > 0 && (
              <div className="space-y-2 rounded-lg bg-muted/40 p-3">
                <p className="text-xs font-medium text-muted-foreground">Sample values (required by Meta)</p>
                {exampleFields.map((f, i) => (
                  <div key={f.id} className="flex items-center gap-2">
                    <span className="w-10 shrink-0 font-mono text-xs text-muted-foreground">{`{{${i + 1}}}`}</span>
                    <Input
                      placeholder={`Example for {{${i + 1}}}`}
                      disabled={isSaving}
                      {...register(`bodyExamples.${i}.value`)}
                    />
                  </div>
                ))}
                {errors.bodyExamples && (
                  <p className="text-xs text-destructive">Provide a sample value for every variable.</p>
                )}
              </div>
            )}
          </SectionCard>

          <SectionCard title="Footer" hint="Optional. Small grey text below the message." step={4}>
            <div className="flex items-center gap-2">
              <Controller
                control={control}
                name="footerEnabled"
                render={({ field }) => (
                  <Switch checked={field.value} onCheckedChange={field.onChange} disabled={isSaving} />
                )}
              />
              <span className="text-sm">Add a footer</span>
            </div>
            {values.footerEnabled && (
              <div className="space-y-1">
                <Input placeholder="e.g. Reply STOP to opt out" disabled={isSaving} {...register("footer")} />
                {errors.footer && <p className="text-xs text-destructive">{errors.footer.message}</p>}
              </div>
            )}
          </SectionCard>

          <SectionCard title="Buttons" hint="Optional. Up to 10 call-to-action or quick-reply buttons." step={5}>
            <ButtonsEditor form={form} disabled={isSaving} />
          </SectionCard>

          <div className="flex items-center justify-between rounded-xl border bg-muted/40 px-5 py-3">
            <p className="text-xs text-muted-foreground">
              Template will be saved as a draft and submitted to Meta for review.
            </p>
            <Button type="submit" disabled={isSaving}>
              {isSaving ? (
                <>
                  <Spinner className="mr-2 size-4" />
                  Saving…
                </>
              ) : (
                <>
                  <Save className="mr-2 h-4 w-4" />
                  {mode === "create" ? "Save draft" : "Save changes"}
                </>
              )}
            </Button>
          </div>
        </div>

        {/* ── Preview ──────────────────────────────────────────────── */}
        <div className="h-fit space-y-3 lg:sticky lg:top-6">
          <div className="flex items-center gap-2 px-1">
            <div className="h-2 w-2 animate-pulse rounded-full bg-emerald-500" />
            <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">Live Preview</p>
          </div>
          <TemplatePreview values={values} />
        </div>
      </div>
    </form>
  );
}
