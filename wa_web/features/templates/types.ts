/** Mirrors wa_api Template enums/DTOs. Keep token casing identical to the backend — the
 *  service switches on these exact lowercase strings (Plan 004). */

export type TemplateStatus = "Draft" | "Pending" | "Approved" | "Rejected" | "Paused" | "Disabled";
export type TemplateCategory = "Marketing";
export type TemplateParameterFormat = "Positional";

/** Header kinds the builder offers (Meta also supports "location"; not built in v1). */
export const HEADER_TYPES = ["none", "text", "image", "document"] as const;
export type HeaderType = (typeof HEADER_TYPES)[number];

/** Marketing button kinds. */
export const BUTTON_TYPES = ["quick_reply", "url", "phone_number", "copy_code"] as const;
export type ButtonType = (typeof BUTTON_TYPES)[number];

export const BUTTON_LABELS: Record<ButtonType, string> = {
  quick_reply: "Quick reply",
  url: "Visit website",
  phone_number: "Call phone number",
  copy_code: "Copy offer code",
};

/** Media kinds a carousel card header can carry. All cards in one carousel share a single kind. */
export const CARD_MEDIA_TYPES = ["image", "video"] as const;
export type CardMediaType = (typeof CARD_MEDIA_TYPES)[number];

/** Button kinds allowed on a carousel card (no copy_code). */
export const CARD_BUTTON_TYPES = ["quick_reply", "url", "phone_number"] as const;
export type CardButtonType = (typeof CARD_BUTTON_TYPES)[number];

/** Carousel limits (Meta media card carousel). */
export const CAROUSEL_MIN_CARDS = 2;
export const CAROUSEL_MAX_CARDS = 10;
export const CAROUSEL_MAX_CARD_BUTTONS = 2;
export const CAROUSEL_CARD_TEXT_MAX = 160;

export interface TemplateHeader {
  type: HeaderType | "video" | "location";
  text?: string | null;
  textExample?: string | null;
  mediaHandle?: string | null;
  /** Id of the persisted sample-media copy (wa_api TemplateMediaSample) for rendering a real preview. */
  mediaPreviewId?: string | null;
}

export interface TemplateBody {
  text: string;
  examples: string[];
}

export interface TemplateFooter {
  text: string;
}

export interface TemplateButton {
  type: ButtonType;
  text: string;
  url?: string | null;
  urlExample?: string | null;
  phoneNumber?: string | null;
  example?: string | null;
}

/** Card header: same media shape as a media header after upload; text fields are unused. */
export interface CarouselCardHeader {
  type: CardMediaType;
  mediaHandle: string;
  mediaPreviewId?: string | null;
}

/** One swipeable card: media + optional short text (no variables) + 1–2 buttons. */
export interface CarouselCard {
  header: CarouselCardHeader;
  body: TemplateBody;
  buttons: TemplateButton[];
}

/** Media card carousel (2–10 cards). When set, the template's header/footer/buttons must be empty. */
export interface TemplateCarousel {
  cards: CarouselCard[];
}

export interface TemplateComponents {
  header?: TemplateHeader | null;
  body: TemplateBody;
  footer?: TemplateFooter | null;
  buttons: TemplateButton[];
  carousel?: TemplateCarousel | null;
}

/** Full template (mirrors wa_api TemplateResponse) — used for detail + the builder. */
export interface Template {
  id: string;
  wabaConnectionId: string;
  displayPhoneNumber?: string | null;
  wabaId: string;
  name: string;
  language: string;
  category: TemplateCategory;
  parameterFormat: TemplateParameterFormat;
  components: TemplateComponents;
  status: TemplateStatus;
  metaTemplateId?: string | null;
  rejectionReason?: string | null;
  submittedAt?: string | null;
  approvedAt?: string | null;
  lastSyncedAt?: string | null;
  isActive: boolean;
  createdAt: string;
}

/** Flat row for the DataTable (its ExportableData constraint forbids nested objects). */
export interface TemplateRow {
  id: string;
  name: string;
  language: string;
  status: TemplateStatus;
  displayPhoneNumber: string | null;
  metaTemplateId: string | null;
  rejectionReason: string | null;
  isActive: boolean;
  createdAt: string;
  approvedAt: string | null;
  /** True for media card carousel templates (drives the "Carousel" badge in the list). */
  isCarousel: boolean;
  // Index signature required by the @data-table ExportableData constraint.
  [key: string]: string | number | boolean | null | undefined;
}

/** Body sent to create/update — components already built from the form. */
export interface TemplatePayload {
  wabaConnectionId: string;
  name: string;
  language: string;
  components: TemplateComponents;
}

export interface TemplateListParams {
  page: number;
  pageSize: number;
  search?: string;
  sortBy?: string;
  sortOrder?: "asc" | "desc";
  status?: string;
}

/** Flattens a full template to the table row shape. */
export function toTemplateRow(t: Template): TemplateRow {
  return {
    id: t.id,
    name: t.name,
    language: t.language,
    status: t.status,
    displayPhoneNumber: t.displayPhoneNumber ?? null,
    metaTemplateId: t.metaTemplateId ?? null,
    rejectionReason: t.rejectionReason ?? null,
    isActive: t.isActive,
    createdAt: t.createdAt,
    approvedAt: t.approvedAt ?? null,
    isCarousel: !!t.components?.carousel?.cards?.length,
  };
}
