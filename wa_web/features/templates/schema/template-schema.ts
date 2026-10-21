import { z } from "zod";
import {
  HEADER_TYPES,
  BUTTON_TYPES,
  CARD_MEDIA_TYPES,
  CARD_BUTTON_TYPES,
  CAROUSEL_MIN_CARDS,
  CAROUSEL_MAX_CARDS,
  CAROUSEL_MAX_CARD_BUTTONS,
  CAROUSEL_CARD_TEXT_MAX,
  type CardButtonType,
  type CarouselCard,
  type Template,
  type TemplateComponents,
  type TemplateHeader,
  type TemplatePayload,
} from "@/features/templates/types";

const NAME_RE = /^[a-z0-9_]+$/;
const PLACEHOLDER_RE = /\{\{(\d+)\}\}/g;
/** Non-global twin of PLACEHOLDER_RE — safe for `.test()` (a /g regex keeps lastIndex between calls). */
const HAS_PLACEHOLDER_RE = /\{\{\d+\}\}/;
/** WhatsApp phone-button numbers must be E.164: '+', country code, up to 15 digits total. */
const E164_RE = /^\+[1-9]\d{1,14}$/;

/** Keeps only '+' and digits so "+94 77 123-4567" → "+94771234567" before validating/sending. */
export function normalizePhone(s: string): string {
  return s.replace(/[^\d+]/g, "");
}

/** Distinct, sorted positional variable indices found in `text` (e.g. "{{1}} {{2}}" → [1,2]). */
export function placeholderIndices(text: string): number[] {
  const nums = new Set<number>();
  for (const m of text.matchAll(PLACEHOLDER_RE)) nums.add(Number(m[1]));
  return [...nums].sort((a, b) => a - b);
}

export function placeholderCount(text: string): number {
  return placeholderIndices(text).length;
}

const EMOJI_RE = /\p{Extended_Pictographic}/u;

/**
 * Counts rendered emoji (grapheme clusters) so a compound emoji — a ZWJ family or a skin-tone
 * variant — counts once, not once per code point. Meta rejects a body with more than 10 emojis;
 * counting code points would false-positive on a few compound emoji. Deliberately lenient (flags
 * and keycaps aren't matched) — better to let Meta catch a rare case than block a valid template.
 */
export function emojiCount(text: string): number {
  if (typeof Intl !== "undefined" && "Segmenter" in Intl) {
    const seg = new Intl.Segmenter(undefined, { granularity: "grapheme" });
    let n = 0;
    for (const { segment } of seg.segment(text)) if (EMOJI_RE.test(segment)) n++;
    return n;
  }
  // Fallback for runtimes without Intl.Segmenter: code-point count.
  return (text.match(/\p{Extended_Pictographic}/gu) ?? []).length;
}

const buttonSchema = z.object({
  type: z.enum(BUTTON_TYPES),
  text: z.string().trim().max(25, "Max 25 characters"),
  url: z.string().trim(),
  urlExample: z.string().trim(),
  phoneNumber: z.string().trim(),
  example: z.string().trim(),
});

/** One carousel card's button. Its type comes from the shared `cardButtonTypes` layout slot, so every
 *  card has the same buttons in the same order by construction; each card only fills its own values. */
const cardButtonSchema = z.object({
  text: z.string().trim().max(25, "Max 25 characters"),
  url: z.string().trim(),
  phoneNumber: z.string().trim(),
});

const cardSchema = z.object({
  mediaHandle: z.string().trim(),
  mediaPreviewId: z.string().trim(),
  text: z.string().trim().max(CAROUSEL_CARD_TEXT_MAX, `Max ${CAROUSEL_CARD_TEXT_MAX} characters`),
  buttons: z.array(cardButtonSchema).max(CAROUSEL_MAX_CARD_BUTTONS),
});

export type BuilderCard = z.infer<typeof cardSchema>;

/**
 * The builder form schema. The component tree is flattened into form fields so react-hook-form
 * + zod paths line up with the inputs. `superRefine` enforces Meta's rules (examples per variable,
 * per-button required fields); the API re-validates as the final authority.
 */
export const builderSchema = z
  .object({
    wabaConnectionId: z.string().min(1, "Select a WhatsApp connection"),
    name: z
      .string()
      .trim()
      .min(1, "Name is required")
      .max(512)
      .regex(NAME_RE, "Lowercase letters, numbers and underscores only — no spaces"),
    language: z.string().trim().min(2, "Select a language"),
    mode: z.enum(["standard", "carousel"]),
    headerType: z.enum(HEADER_TYPES),
    headerText: z.string().trim().max(60, "Max 60 characters"),
    headerTextExample: z.string().trim(),
    headerMediaHandle: z.string().trim(),
    headerMediaPreviewId: z.string().trim(),
    body: z.string().trim().min(1, "Body text is required").max(1024, "Max 1024 characters"),
    bodyExamples: z.array(z.object({ value: z.string().trim() })),
    footerEnabled: z.boolean(),
    footer: z.string().trim().max(60, "Max 60 characters"),
    buttons: z.array(buttonSchema).max(10, "At most 10 buttons"),
    // Carousel mode only (ignored in standard mode): one media kind and one button layout shared by all cards.
    cardMediaType: z.enum(CARD_MEDIA_TYPES),
    cardButtonTypes: z.array(z.enum(CARD_BUTTON_TYPES)).max(CAROUSEL_MAX_CARD_BUTTONS),
    cards: z.array(cardSchema).max(CAROUSEL_MAX_CARDS, `At most ${CAROUSEL_MAX_CARDS} cards`),
  })
  .superRefine((v, ctx) => {
    const isCarousel = v.mode === "carousel";

    // ── Header ────────────────────────────────────────────────────────────
    // Header/footer/top-level buttons are not allowed on a carousel, so their rules only apply in
    // standard mode (their values are kept in the form so switching back loses nothing).
    if (!isCarousel && v.headerType === "text") {
      const count = placeholderCount(v.headerText);
      if (!v.headerText)
        ctx.addIssue({ code: "custom", path: ["headerText"], message: "Header text is required" });
      else if (count > 1)
        ctx.addIssue({ code: "custom", path: ["headerText"], message: "Header allows at most one variable {{1}}" });
      else if (count === 1 && !v.headerTextExample)
        ctx.addIssue({ code: "custom", path: ["headerTextExample"], message: "Provide an example for the header variable" });
    }
    if (!isCarousel && (v.headerType === "image" || v.headerType === "document") && !v.headerMediaHandle)
      ctx.addIssue({ code: "custom", path: ["headerMediaHandle"], message: "Upload a sample media file" });

    // ── Body: Meta content rules + variables/examples ─────────────────────
    // Meta rejects a body with no literal text, more than two consecutive newlines, or more than
    // 10 emojis (its error OR's all three). Checked here — and re-checked server-side — so the
    // draft can't be saved in a state that would only fail later at submit time.
    const idx = placeholderIndices(v.body);
    const contiguous = idx.every((n, i) => n === i + 1);
    const literal = v.body.replace(/\{\{\d+\}\}/g, "").trim();
    // Meta's variable-density rule: (words + variables) ≥ 3·variables + 1. `\s` already covers
    // newlines, so newline-separated words tokenize correctly; filter empties so a stray space
    // doesn't inflate the count. P is the distinct variable count (lenient on the rare repeat).
    const tokenCount = v.body.split(/\s+/).filter(Boolean).length;
    const minTokens = 3 * idx.length + 1;
    if (v.body && !literal) {
      ctx.addIssue({ code: "custom", path: ["body"], message: "The body needs some text, not just variables." });
    } else if (v.body.replace(/\r\n?/g, "\n").includes("\n\n\n")) {
      ctx.addIssue({ code: "custom", path: ["body"], message: "Remove the extra line breaks — no more than two newlines in a row." });
    } else if (emojiCount(v.body) > 10) {
      ctx.addIssue({ code: "custom", path: ["body"], message: "Use at most 10 emojis in the body." });
    } else if (!contiguous) {
      ctx.addIssue({ code: "custom", path: ["body"], message: "Variables must be numbered consecutively from {{1}}" });
    } else if (idx.length > 0 && tokenCount < minTokens) {
      const deficit = minTokens - tokenCount;
      ctx.addIssue({
        code: "custom",
        path: ["body"],
        message: `Too many variables for the message length — add ${deficit} more word${deficit === 1 ? "" : "s"} or remove a variable.`,
      });
    } else {
      idx.forEach((_, i) => {
        if (!v.bodyExamples[i]?.value)
          ctx.addIssue({ code: "custom", path: ["bodyExamples", i, "value"], message: "Example required" });
      });
    }

    // ── Footer ────────────────────────────────────────────────────────────
    if (!isCarousel && v.footerEnabled && !v.footer)
      ctx.addIssue({ code: "custom", path: ["footer"], message: "Footer text is required (or turn it off)" });

    // ── Buttons ───────────────────────────────────────────────────────────
    if (!isCarousel) v.buttons.forEach((b, i) => {
      if (b.type !== "copy_code" && !b.text)
        ctx.addIssue({ code: "custom", path: ["buttons", i, "text"], message: "Button text is required" });
      if (b.type === "url") {
        if (!b.url) ctx.addIssue({ code: "custom", path: ["buttons", i, "url"], message: "URL is required" });
        else if (/\{\{\d+\}\}/.test(b.url) && !b.urlExample)
          ctx.addIssue({ code: "custom", path: ["buttons", i, "urlExample"], message: "Provide an example value" });
      }
      if (b.type === "phone_number") {
        if (!b.phoneNumber)
          ctx.addIssue({ code: "custom", path: ["buttons", i, "phoneNumber"], message: "Phone number is required" });
        else if (!E164_RE.test(normalizePhone(b.phoneNumber)))
          ctx.addIssue({
            code: "custom",
            path: ["buttons", i, "phoneNumber"],
            message: "Use international format, e.g. +14155552671 — country code, no spaces.",
          });
      }
      if (b.type === "copy_code" && !b.example)
        ctx.addIssue({ code: "custom", path: ["buttons", i, "example"], message: "Sample code is required" });
    });

    // ── Carousel cards ────────────────────────────────────────────────────
    if (isCarousel) {
      if (v.cards.length < CAROUSEL_MIN_CARDS)
        ctx.addIssue({ code: "custom", path: ["cards"], message: `A carousel needs at least ${CAROUSEL_MIN_CARDS} cards` });

      const layout = v.cardButtonTypes;
      if (layout.length < 1)
        ctx.addIssue({ code: "custom", path: ["cardButtonTypes"], message: "Every card needs 1 or 2 buttons" });

      // Card text is optional, but all-or-nothing: if one card has text, every card needs it.
      const anyText = v.cards.some((c) => c.text);

      v.cards.forEach((c, i) => {
        if (!c.mediaHandle)
          ctx.addIssue({ code: "custom", path: ["cards", i, "mediaHandle"], message: `Upload a sample ${v.cardMediaType}` });

        if (HAS_PLACEHOLDER_RE.test(c.text))
          ctx.addIssue({ code: "custom", path: ["cards", i, "text"], message: "Variables like {{1}} aren't allowed in card text" });
        else if (anyText && !c.text)
          ctx.addIssue({ code: "custom", path: ["cards", i, "text"], message: "Add text — if any card has text, every card needs it" });

        layout.forEach((type, j) => {
          const b = c.buttons[j];
          if (!b || !b.text)
            ctx.addIssue({ code: "custom", path: ["cards", i, "buttons", j, "text"], message: "Button text is required" });
          if (!b) return;
          if (type === "url") {
            if (!b.url) ctx.addIssue({ code: "custom", path: ["cards", i, "buttons", j, "url"], message: "URL is required" });
            else if (HAS_PLACEHOLDER_RE.test(b.url))
              ctx.addIssue({
                code: "custom",
                path: ["cards", i, "buttons", j, "url"],
                message: "Card URLs must be static — remove the {{1}} variable",
              });
          }
          if (type === "phone_number") {
            if (!b.phoneNumber)
              ctx.addIssue({ code: "custom", path: ["cards", i, "buttons", j, "phoneNumber"], message: "Phone number is required" });
            else if (!E164_RE.test(normalizePhone(b.phoneNumber)))
              ctx.addIssue({
                code: "custom",
                path: ["cards", i, "buttons", j, "phoneNumber"],
                message: "Use international format, e.g. +14155552671 — country code, no spaces.",
              });
          }
        });
      });
    }
  });

export type BuilderFormValues = z.infer<typeof builderSchema>;

/** Empty builder state for the "New template" page. */
export function emptyBuilderValues(): BuilderFormValues {
  return {
    wabaConnectionId: "",
    name: "",
    language: "en_US",
    mode: "standard",
    headerType: "none",
    headerText: "",
    headerTextExample: "",
    headerMediaHandle: "",
    headerMediaPreviewId: "",
    body: "",
    bodyExamples: [],
    footerEnabled: false,
    footer: "",
    // Seed a one-tap opt-out by default (Meta best practice). Editable/removable here; the API also
    // guarantees a "Stop" quick-reply on every marketing template at submit time, so it can't be lost.
    buttons: [
      { type: "quick_reply", text: "Stop", url: "", urlExample: "", phoneNumber: "", example: "" },
    ],
    cardMediaType: "image",
    cardButtonTypes: [],
    cards: [],
  };
}

export const emptyCardButton = () => ({ text: "", url: "", phoneNumber: "" });

/** A blank card whose button slots match the shared layout. */
export function emptyCard(layout: CardButtonType[]): BuilderCard {
  return { mediaHandle: "", mediaPreviewId: "", text: "", buttons: layout.map(emptyCardButton) };
}

/** Seeds carousel mode the first time it is chosen: two blank cards with one quick-reply slot each. */
export function initialCarouselState(): Pick<BuilderFormValues, "cardMediaType" | "cardButtonTypes" | "cards"> {
  const layout: CardButtonType[] = ["quick_reply"];
  return { cardMediaType: "image", cardButtonTypes: layout, cards: [emptyCard(layout), emptyCard(layout)] };
}

/**
 * TESTING ONLY — a pre-filled, structurally-valid draft so a template can be created in one click
 * during QA. The body has no variables (so no examples are required) which keeps it the smallest
 * payload that passes both this schema and the API's `Validate`. The WhatsApp number is left blank
 * and auto-selected in the form (first active connection); the form also gives it a unique name so
 * repeated test-creates don't collide on (name, language).
 *
 * To go back to a blank form, swap this for `emptyBuilderValues()` in `template-builder.tsx` and
 * delete the seeding effects in `template-builder-form.tsx`.
 */
export function testBuilderValues(): BuilderFormValues {
  return {
    ...emptyBuilderValues(),
    name: "test_template",
    body: "Hi there! This is a sample WhatsApp template created for testing.",
    footerEnabled: true,
    footer: "Reply STOP to unsubscribe",
  };
}

/** Builds the API payload (normalized component tree) from validated form values. */
export function buildTemplatePayload(v: BuilderFormValues): TemplatePayload {
  if (v.mode === "carousel") {
    const cards: CarouselCard[] = v.cards.map((c) => ({
      header: { type: v.cardMediaType, mediaHandle: c.mediaHandle, mediaPreviewId: c.mediaPreviewId || null },
      body: { text: c.text, examples: [] },
      buttons: v.cardButtonTypes.map((type, j) => {
        const b = c.buttons[j] ?? emptyCardButton();
        return {
          type,
          text: b.text,
          url: type === "url" ? b.url : null,
          urlExample: null,
          phoneNumber: type === "phone_number" ? normalizePhone(b.phoneNumber) : null,
          example: null,
        };
      }),
    }));
    // Meta forbids a header, footer or top-level buttons alongside a carousel; no "Stop" opt-out is added.
    const components: TemplateComponents = {
      header: null,
      body: {
        text: v.body,
        examples: placeholderIndices(v.body).map((_, i) => v.bodyExamples[i]?.value ?? ""),
      },
      footer: null,
      buttons: [],
      carousel: { cards },
    };
    return { wabaConnectionId: v.wabaConnectionId, name: v.name, language: v.language, components };
  }

  const header: TemplateHeader | null =
    v.headerType === "none"
      ? null
      : v.headerType === "text"
        ? { type: "text", text: v.headerText, textExample: placeholderCount(v.headerText) === 1 ? v.headerTextExample : null }
        : { type: v.headerType, mediaHandle: v.headerMediaHandle, mediaPreviewId: v.headerMediaPreviewId || null };

  const components: TemplateComponents = {
    header,
    body: {
      text: v.body,
      examples: placeholderIndices(v.body).map((_, i) => v.bodyExamples[i]?.value ?? ""),
    },
    footer: v.footerEnabled && v.footer ? { text: v.footer } : null,
    buttons: v.buttons.map((b) => ({
      type: b.type,
      text: b.type === "copy_code" ? "" : b.text,
      url: b.type === "url" ? b.url : null,
      urlExample: b.type === "url" && /\{\{\d+\}\}/.test(b.url) ? b.urlExample : null,
      phoneNumber: b.type === "phone_number" ? normalizePhone(b.phoneNumber) : null,
      example: b.type === "copy_code" ? b.example : null,
    })),
    carousel: null,
  };

  return { wabaConnectionId: v.wabaConnectionId, name: v.name, language: v.language, components };
}

/** Maps a loaded template back into builder form values (for editing a draft). */
export function templateToFormValues(t: Template): BuilderFormValues {
  const h = t.components.header;
  const headerType = (h?.type ?? "none") as BuilderFormValues["headerType"];
  const cards = t.components.carousel?.cards ?? [];
  const isCarousel = cards.length > 0;
  // The button layout (types per slot) is shared by every card, so the first card defines it.
  const cardButtonTypes = (cards[0]?.buttons ?? [])
    .map((b) => b.type)
    .filter((type): type is CardButtonType => (CARD_BUTTON_TYPES as readonly string[]).includes(type));
  return {
    wabaConnectionId: t.wabaConnectionId,
    name: t.name,
    language: t.language,
    mode: isCarousel ? "carousel" : "standard",
    headerType: HEADER_TYPES.includes(headerType) ? headerType : "none",
    headerText: h?.text ?? "",
    headerTextExample: h?.textExample ?? "",
    headerMediaHandle: h?.mediaHandle ?? "",
    headerMediaPreviewId: h?.mediaPreviewId ?? "",
    body: t.components.body?.text ?? "",
    bodyExamples: (t.components.body?.examples ?? []).map((value) => ({ value })),
    footerEnabled: !!t.components.footer,
    footer: t.components.footer?.text ?? "",
    buttons: (t.components.buttons ?? []).map((b) => ({
      type: b.type,
      text: b.text ?? "",
      url: b.url ?? "",
      urlExample: b.urlExample ?? "",
      phoneNumber: b.phoneNumber ?? "",
      example: b.example ?? "",
    })),
    cardMediaType: cards[0]?.header.type === "video" ? "video" : "image",
    cardButtonTypes,
    cards: cards.map((c) => ({
      mediaHandle: c.header.mediaHandle ?? "",
      mediaPreviewId: c.header.mediaPreviewId ?? "",
      text: c.body?.text ?? "",
      buttons: cardButtonTypes.map((_, j) => ({
        text: c.buttons[j]?.text ?? "",
        url: c.buttons[j]?.url ?? "",
        phoneNumber: c.buttons[j]?.phoneNumber ?? "",
      })),
    })),
  };
}
