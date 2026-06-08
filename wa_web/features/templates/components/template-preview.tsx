"use client";

import { FileText, Image as ImageIcon, Video, ExternalLink, Phone, Copy, CornerUpLeft } from "lucide-react";
import type { BuilderFormValues } from "@/features/templates/schema/template-schema";

function fill(text: string, examples: string[]): string {
  return text.replace(/\{\{(\d+)\}\}/g, (_, n) => examples[Number(n) - 1] || `{{${n}}}`);
}

const MEDIA_ICON = { image: ImageIcon, video: Video, document: FileText } as const;

export function TemplatePreview({ values }: { values: BuilderFormValues }) {
  const bodyExamples = values.bodyExamples.map((e) => e.value);
  const hasBody = values.body.trim().length > 0;

  return (
    <div className="rounded-2xl border bg-[#e5ddd5] p-4 dark:bg-zinc-800">
      <p className="mb-3 text-center text-xs font-medium text-zinc-500 dark:text-zinc-400">Preview</p>

      <div className="mx-auto max-w-[320px] space-y-1">
        <div className="rounded-lg rounded-tl-none bg-white p-2.5 shadow-sm dark:bg-zinc-700">
          {/* Header */}
          {values.headerType === "text" && values.headerText && (
            <p className="mb-1 font-semibold text-zinc-900 dark:text-zinc-50">
              {fill(values.headerText, values.headerTextExample ? [values.headerTextExample] : [])}
            </p>
          )}
          {(values.headerType === "image" || values.headerType === "video" || values.headerType === "document") && (
            <div className="mb-2 flex h-28 items-center justify-center rounded-md bg-zinc-100 text-zinc-400 dark:bg-zinc-600 dark:text-zinc-300">
              {(() => {
                const Icon = MEDIA_ICON[values.headerType];
                return <Icon className="h-8 w-8" />;
              })()}
            </div>
          )}

          {/* Body */}
          {hasBody ? (
            <p className="whitespace-pre-wrap break-words text-sm text-zinc-800 dark:text-zinc-100">
              {fill(values.body, bodyExamples)}
            </p>
          ) : (
            <p className="text-sm italic text-zinc-400">Your message body appears here…</p>
          )}

          {/* Footer */}
          {values.footerEnabled && values.footer && (
            <p className="mt-1.5 text-xs text-zinc-400 dark:text-zinc-400">{values.footer}</p>
          )}

          <p className="mt-1 text-right text-[10px] text-zinc-400">
            {new Intl.DateTimeFormat("en-US", { hour: "numeric", minute: "2-digit" }).format(
              new Date(2024, 0, 1, 10, 30)
            )}
          </p>
        </div>

        {/* Buttons */}
        {values.buttons.length > 0 && (
          <div className="space-y-1">
            {values.buttons.map((b, i) => {
              const Icon =
                b.type === "url" ? ExternalLink : b.type === "phone_number" ? Phone : b.type === "copy_code" ? Copy : CornerUpLeft;
              const label =
                b.type === "copy_code" ? "Copy offer code" : b.text || "Button";
              return (
                <div
                  key={i}
                  className="flex items-center justify-center gap-2 rounded-lg bg-white py-2 text-sm font-medium text-sky-600 shadow-sm dark:bg-zinc-700 dark:text-sky-400"
                >
                  <Icon className="h-4 w-4" />
                  {label}
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
