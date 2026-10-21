"use client";

import { FileText, Image as ImageIcon, Video, ExternalLink, Phone, Copy, CornerUpLeft } from "lucide-react";
import { Carousel, CarouselContent, CarouselItem } from "@/components/ui/carousel";
import type { BuilderFormValues } from "@/features/templates/schema/template-schema";
import type { CardButtonType } from "@/features/templates/types";
import { renderWhatsAppText } from "@/lib/whatsapp-text";

function fill(text: string, examples: string[]): string {
  return text.replace(/\{\{(\d+)\}\}/g, (_, n) => examples[Number(n) - 1] || `{{${n}}}`);
}

const MEDIA_ICON = { image: ImageIcon, document: FileText } as const;

const BUTTON_ICON = { url: ExternalLink, phone_number: Phone, quick_reply: CornerUpLeft } as const;

/** Swipeable card row, like WhatsApp: each card is media, optional text, then its button(s); the next card peeks in. */
function CarouselPreview({ values }: { values: BuilderFormValues }) {
  const layout: CardButtonType[] = values.cardButtonTypes;
  const isVideo = values.cardMediaType === "video";
  const PlaceholderIcon = isVideo ? Video : ImageIcon;

  return (
    <Carousel opts={{ align: "start", dragFree: true }} className="pt-1">
      <CarouselContent className="-ml-1.5">
        {values.cards.map((c, i) => {
          const src = c.mediaPreviewId ? `/api/templates/media-preview/${c.mediaPreviewId}` : null;
          return (
            <CarouselItem key={i} className="basis-[68%] pl-1.5">
              <div className="flex h-full flex-col overflow-hidden rounded-lg bg-white shadow-sm dark:bg-zinc-700">
                {src && isVideo ? (
                  <video src={`${src}#t=0.1`} muted playsInline preload="metadata" className="aspect-[4/3] w-full object-cover" />
                ) : src ? (
                  // eslint-disable-next-line @next/next/no-img-element -- blob served by a dynamic API route, not optimizable
                  <img src={src} alt={`Card ${i + 1} media`} className="aspect-[4/3] w-full object-cover" />
                ) : (
                  <div className="flex aspect-[4/3] w-full items-center justify-center bg-zinc-100 text-zinc-400 dark:bg-zinc-600 dark:text-zinc-300">
                    <PlaceholderIcon className="h-8 w-8" />
                  </div>
                )}
                {c.text && (
                  <p className="whitespace-pre-wrap break-words px-2.5 pt-2 text-sm text-zinc-800 dark:text-zinc-100">
                    {renderWhatsAppText(c.text)}
                  </p>
                )}
                <div className="mt-auto pt-2">
                  {layout.map((type, j) => {
                    const Icon = BUTTON_ICON[type];
                    return (
                      <div
                        key={j}
                        className="flex items-center justify-center gap-2 border-t border-zinc-200 py-2 text-sm font-medium text-sky-600 dark:border-zinc-600 dark:text-sky-400"
                      >
                        <Icon className="h-4 w-4" />
                        <span className="truncate">{c.buttons[j]?.text || "Button"}</span>
                      </div>
                    );
                  })}
                </div>
              </div>
            </CarouselItem>
          );
        })}
      </CarouselContent>
    </Carousel>
  );
}

export function TemplatePreview({ values }: { values: BuilderFormValues }) {
  const bodyExamples = values.bodyExamples.map((e) => e.value);
  const hasBody = values.body.trim().length > 0;
  const isCarousel = values.mode === "carousel";

  return (
    <div className="rounded-2xl border bg-[#e5ddd5] p-4 dark:bg-zinc-800">
      <p className="mb-3 text-center text-xs font-medium text-zinc-500 dark:text-zinc-400">Preview</p>

      <div className="mx-auto max-w-[320px] space-y-1">
        <div className="rounded-lg rounded-tl-none bg-white p-2.5 shadow-sm dark:bg-zinc-700">
          {/* Header */}
          {!isCarousel && values.headerType === "text" && values.headerText && (
            <p className="mb-1 font-semibold text-zinc-900 dark:text-zinc-50">
              {fill(values.headerText, values.headerTextExample ? [values.headerTextExample] : [])}
            </p>
          )}
          {isCarousel ? null : values.headerType === "image" && values.headerMediaPreviewId ? (
            // eslint-disable-next-line @next/next/no-img-element -- blob served by a dynamic API route, not optimizable
            <img
              src={`/api/templates/media-preview/${values.headerMediaPreviewId}`}
              alt="Header media"
              className="mb-2 h-28 w-full rounded-md object-cover"
            />
          ) : (
            (values.headerType === "image" || values.headerType === "document") && (
              <div className="mb-2 flex h-28 items-center justify-center rounded-md bg-zinc-100 text-zinc-400 dark:bg-zinc-600 dark:text-zinc-300">
                {(() => {
                  const Icon = MEDIA_ICON[values.headerType];
                  return <Icon className="h-8 w-8" />;
                })()}
              </div>
            )
          )}

          {/* Body */}
          {hasBody ? (
            <p className="whitespace-pre-wrap break-words text-sm text-zinc-800 dark:text-zinc-100">
              {renderWhatsAppText(fill(values.body, bodyExamples))}
            </p>
          ) : (
            <p className="text-sm italic text-zinc-400">Your message body appears here…</p>
          )}

          {/* Footer */}
          {!isCarousel && values.footerEnabled && values.footer && (
            <p className="mt-1.5 text-xs text-zinc-400 dark:text-zinc-400">{values.footer}</p>
          )}

          <p className="mt-1 text-right text-[10px] text-zinc-400">
            {new Intl.DateTimeFormat(undefined, { hour: "numeric", minute: "2-digit" }).format(
              new Date(2024, 0, 1, 10, 30)
            )}
          </p>
        </div>

        {/* Cards */}
        {isCarousel && values.cards.length > 0 && <CarouselPreview values={values} />}

        {/* Buttons */}
        {!isCarousel && values.buttons.length > 0 && (
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
