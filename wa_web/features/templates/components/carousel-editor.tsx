"use client";

import { useFieldArray, useWatch, type UseFormReturn } from "react-hook-form";
import { ChevronLeft, ChevronRight, Copy, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  BUTTON_LABELS,
  CARD_BUTTON_TYPES,
  CARD_MEDIA_TYPES,
  CAROUSEL_CARD_TEXT_MAX,
  CAROUSEL_MAX_CARD_BUTTONS,
  CAROUSEL_MAX_CARDS,
  CAROUSEL_MIN_CARDS,
  type CardButtonType,
  type CardMediaType,
} from "@/features/templates/types";
import {
  emptyCard,
  emptyCardButton,
  type BuilderFormValues,
} from "@/features/templates/schema/template-schema";
import { MediaUpload } from "./media-upload";

const MEDIA_LABELS: Record<CardMediaType, string> = { image: "Image", video: "Video" };

type Props = { form: UseFormReturn<BuilderFormValues>; disabled?: boolean };

/**
 * The button layout (how many buttons, and of which type, per card) is edited once and applies to
 * every card — Meta requires identical layouts. Each card then only fills its own label/URL/phone
 * per slot, so adding, removing or retyping a slot here rewrites every card's `buttons` to match.
 */
export function CardButtonsLayoutEditor({ form, disabled }: Props) {
  const { control, setValue, getValues, formState: { errors } } = form;
  const layout = useWatch({ control, name: "cardButtonTypes" }) ?? [];

  const applyLayout = (next: CardButtonType[], retypedSlot?: number) => {
    setValue("cardButtonTypes", next, { shouldDirty: true });
    getValues("cards").forEach((c, i) => {
      setValue(
        `cards.${i}.buttons`,
        next.map((_, j) => {
          const existing = c.buttons[j] ?? emptyCardButton();
          // A retyped slot keeps its label but drops the now-meaningless URL/phone value.
          return j === retypedSlot ? { ...existing, url: "", phoneNumber: "" } : existing;
        }),
        { shouldDirty: true }
      );
    });
  };

  return (
    <div className="space-y-3">
      {layout.map((type, j) => (
        <div key={j} className="flex items-center gap-2 rounded-lg border p-3">
          <span className="w-16 shrink-0 text-xs font-medium text-muted-foreground">Button {j + 1}</span>
          <Select
            value={type}
            onValueChange={(t) => applyLayout(layout.map((x, k) => (k === j ? (t as CardButtonType) : x)), j)}
            disabled={disabled}
          >
            <SelectTrigger className="h-8 w-52">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {CARD_BUTTON_TYPES.map((t) => (
                <SelectItem key={t} value={t}>
                  {BUTTON_LABELS[t]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="ml-auto h-8 w-8"
            disabled={disabled || layout.length <= 1}
            onClick={() => applyLayout(layout.filter((_, k) => k !== j))}
            aria-label="Remove button from all cards"
          >
            <Trash2 className="h-4 w-4" />
          </Button>
        </div>
      ))}

      {errors.cardButtonTypes?.message && (
        <p className="text-xs text-destructive">{errors.cardButtonTypes.message}</p>
      )}

      <Button
        type="button"
        variant="outline"
        size="sm"
        disabled={disabled || layout.length >= CAROUSEL_MAX_CARD_BUTTONS}
        onClick={() => applyLayout([...layout, "quick_reply"])}
      >
        <Plus className="mr-1 h-4 w-4" />
        Add button
      </Button>
      <p className="text-xs text-muted-foreground">
        Every card gets these buttons in this order (1 or 2). Copy-code buttons aren&apos;t available on cards, and
        URLs must be static.
      </p>
    </div>
  );
}

/** Card list editor: shared media kind, add / remove / reorder / duplicate, and per-card content. */
export function CarouselCardsEditor({ form, disabled }: Props) {
  const { control, register, setValue, getValues, formState: { errors } } = form;
  const { fields, append, insert, remove, move } = useFieldArray({ control, name: "cards" });
  const mediaType = useWatch({ control, name: "cardMediaType" });
  const layout = useWatch({ control, name: "cardButtonTypes" }) ?? [];
  const cards = useWatch({ control, name: "cards" }) ?? [];

  // All cards must share one media kind, so changing it applies to every card; samples uploaded
  // for the old kind are no longer valid and are cleared.
  const changeMediaType = (t: CardMediaType) => {
    if (t === mediaType) return;
    const hadMedia = getValues("cards").some((c) => c.mediaHandle);
    setValue("cardMediaType", t, { shouldDirty: true });
    getValues("cards").forEach((_, i) => {
      setValue(`cards.${i}.mediaHandle`, "", { shouldDirty: true });
      setValue(`cards.${i}.mediaPreviewId`, "", { shouldDirty: true });
    });
    if (hadMedia) toast.info(`Cards now use ${t}s — upload a sample ${t} for each card.`);
  };

  const atMax = fields.length >= CAROUSEL_MAX_CARDS;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <Label className="text-xs">Card media</Label>
        <Select value={mediaType} onValueChange={(t) => changeMediaType(t as CardMediaType)} disabled={disabled}>
          <SelectTrigger className="h-8 w-32">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {CARD_MEDIA_TYPES.map((t) => (
              <SelectItem key={t} value={t}>
                {MEDIA_LABELS[t]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <span className="text-xs text-muted-foreground">
          Applies to all cards · {fields.length}/{CAROUSEL_MAX_CARDS} cards
        </span>
      </div>

      {errors.cards?.message && <p className="text-xs text-destructive">{errors.cards.message}</p>}

      {fields.map((field, i) => {
        const cardErr = errors.cards?.[i];
        const card = cards[i];
        const textLen = card?.text?.length ?? 0;
        return (
          <div key={field.id} className="space-y-3 rounded-lg border p-3">
            <div className="flex items-center justify-between gap-2">
              <p className="text-sm font-medium">Card {i + 1}</p>
              <div className="flex items-center">
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="h-8 w-8"
                  disabled={disabled || i === 0}
                  onClick={() => move(i, i - 1)}
                  aria-label="Move card left"
                >
                  <ChevronLeft className="h-4 w-4" />
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="h-8 w-8"
                  disabled={disabled || i === fields.length - 1}
                  onClick={() => move(i, i + 1)}
                  aria-label="Move card right"
                >
                  <ChevronRight className="h-4 w-4" />
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="h-8 w-8"
                  disabled={disabled || atMax}
                  onClick={() => insert(i + 1, structuredClone(getValues(`cards.${i}`)))}
                  aria-label="Duplicate card"
                >
                  <Copy className="h-4 w-4" />
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="h-8 w-8"
                  disabled={disabled || fields.length <= CAROUSEL_MIN_CARDS}
                  onClick={() => remove(i)}
                  aria-label="Remove card"
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>
            </div>

            <div className="space-y-1">
              <MediaUpload
                key={`${field.id}-${mediaType}`}
                mediaType={mediaType}
                purpose="card"
                value={card?.mediaHandle ?? ""}
                onChange={(h, previewId) => {
                  setValue(`cards.${i}.mediaHandle`, h, { shouldValidate: true, shouldDirty: true });
                  setValue(`cards.${i}.mediaPreviewId`, previewId, { shouldDirty: true });
                }}
                disabled={disabled}
              />
              {cardErr?.mediaHandle && <p className="text-xs text-destructive">{cardErr.mediaHandle.message}</p>}
            </div>

            <div className="space-y-1">
              <div className="flex items-center justify-between">
                <Label className="text-xs">Card text (optional)</Label>
                <span className="text-xs text-muted-foreground">
                  {textLen}/{CAROUSEL_CARD_TEXT_MAX}
                </span>
              </div>
              <Textarea
                rows={2}
                placeholder="Short caption shown under the media — no variables"
                disabled={disabled}
                {...register(`cards.${i}.text`)}
              />
              {cardErr?.text && <p className="text-xs text-destructive">{cardErr.text.message}</p>}
            </div>

            <div className="space-y-2">
              {layout.map((type, j) => {
                const btnErr = cardErr?.buttons?.[j];
                return (
                  <div key={j} className="space-y-2 rounded-md bg-muted/40 p-2.5">
                    <p className="text-xs font-medium text-muted-foreground">
                      Button {j + 1} · {BUTTON_LABELS[type]}
                    </p>
                    <div>
                      <Input
                        placeholder="Button text"
                        disabled={disabled}
                        {...register(`cards.${i}.buttons.${j}.text`)}
                      />
                      {btnErr?.text && <p className="mt-1 text-xs text-destructive">{btnErr.text.message}</p>}
                    </div>
                    {type === "url" && (
                      <div>
                        <Input
                          placeholder="https://example.com/offer"
                          disabled={disabled}
                          {...register(`cards.${i}.buttons.${j}.url`)}
                        />
                        {btnErr?.url && <p className="mt-1 text-xs text-destructive">{btnErr.url.message}</p>}
                      </div>
                    )}
                    {type === "phone_number" && (
                      <div>
                        <Input
                          placeholder="+14155552671"
                          disabled={disabled}
                          {...register(`cards.${i}.buttons.${j}.phoneNumber`)}
                        />
                        {btnErr?.phoneNumber && (
                          <p className="mt-1 text-xs text-destructive">{btnErr.phoneNumber.message}</p>
                        )}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          </div>
        );
      })}

      <Button
        type="button"
        variant="outline"
        size="sm"
        disabled={disabled || atMax}
        onClick={() => append(emptyCard(layout))}
      >
        <Plus className="mr-1 h-4 w-4" />
        Add card
      </Button>
    </div>
  );
}
