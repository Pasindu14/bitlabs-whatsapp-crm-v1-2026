import { z } from "zod";
import {
  HEADER_TYPES,
  BUTTON_TYPES,
  type Template,
  type TemplateComponents,
  type TemplateHeader,
  type TemplatePayload,
} from "@/features/templates/types";

const NAME_RE = /^[a-z0-9_]+$/;
const PLACEHOLDER_RE = /\{\{(\d+)\}\}/g;
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
  })
  .superRefine((v, ctx) => {
    // ── Header ────────────────────────────────────────────────────────────
    if (v.headerType === "text") {
      const count = placeholderCount(v.headerText);
      if (!v.headerText)
        ctx.addIssue({ code: "custom", path: ["headerText"], message: "Header text is required" });
      else if (count > 1)
        ctx.addIssue({ code: "custom", path: ["headerText"], message: "Header allows at most one variable {{1}}" });
      else if (count === 1 && !v.headerTextExample)
        ctx.addIssue({ code: "custom", path: ["headerTextExample"], message: "Provide an example for the header variable" });
    }
    if ((v.headerType === "image" || v.headerType === "document") && !v.headerMediaHandle)
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
    if (v.footerEnabled && !v.footer)
      ctx.addIssue({ code: "custom", path: ["footer"], message: "Footer text is required (or turn it off)" });

    // ── Buttons ───────────────────────────────────────────────────────────
    v.buttons.forEach((b, i) => {
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
  });

export type BuilderFormValues = z.infer<typeof builderSchema>;

/** Empty builder state for the "New template" page. */
export function emptyBuilderValues(): BuilderFormValues {
  return {
    wabaConnectionId: "",
    name: "",
    language: "en_US",
    headerType: "none",
    headerText: "",
    headerTextExample: "",
    headerMediaHandle: "",
    headerMediaPreviewId: "",
    body: "",
    bodyExamples: [],
    footerEnabled: false,
    footer: "",
    buttons: [],
  };
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
  };

  return { wabaConnectionId: v.wabaConnectionId, name: v.name, language: v.language, components };
}

/** Maps a loaded template back into builder form values (for editing a draft). */
export function templateToFormValues(t: Template): BuilderFormValues {
  const h = t.components.header;
  const headerType = (h?.type ?? "none") as BuilderFormValues["headerType"];
  return {
    wabaConnectionId: t.wabaConnectionId,
    name: t.name,
    language: t.language,
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
  };
}
