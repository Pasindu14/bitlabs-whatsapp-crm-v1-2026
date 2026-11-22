/**
 * Trigger kinds, mirroring wa_api AutoReplyTrigger. Serialized as the enum NAME
 * (the API uses JsonStringEnumConverter), so these strings are the wire contract.
 */
export const AUTO_REPLY_TRIGGERS = [
  "Keyword",
  "Contains",
  "Exact",
  "Regex",
  "Welcome",
  "Fallback",
] as const;

export type AutoReplyTrigger = (typeof AUTO_REPLY_TRIGGERS)[number];

/** Triggers that match on POSITION rather than content — their terms are ignored. */
export const TERMLESS_TRIGGERS: readonly AutoReplyTrigger[] = ["Welcome", "Fallback"];

export const TRIGGER_LABELS: Record<AutoReplyTrigger, string> = {
  Keyword: "Keyword",
  Contains: "Contains",
  Exact: "Exact match",
  Regex: "Regex",
  Welcome: "First message",
  Fallback: "Catch-all",
};

export const TRIGGER_HINTS: Record<AutoReplyTrigger, string> = {
  Keyword: "A term appears as a whole word — “hi” matches “hi there” but not “this”.",
  Contains: "A term appears anywhere, including inside a longer word.",
  Exact: "The whole message equals a term.",
  Regex: "A term is a regular expression matched against the message.",
  Welcome: "Only the customer's first message in a conversation. Terms are ignored.",
  Fallback: "Matches anything. Give it a high priority number so it runs last.",
};

/** One auto-reply rule as returned by wa_api (mirrors AutoReplyRuleResponse). */
export interface AutoReplyRule {
  id: string;
  name: string;
  trigger: AutoReplyTrigger;
  terms: string[];
  replyBody: string;
  priority: number;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
  // Index signature required by the @data-table ExportableData constraint.
  [key: string]: string | number | boolean | string[] | null | undefined;
}

// ── The AI assistant ──────────────────────────────────────────────────────

/** The assistant's business profile (mirrors wa_api BotProfileResponse). */
export interface BotProfile {
  id: string | null;
  /** False until the tenant saves for the first time — the API returns defaults, not a 404. */
  exists: boolean;
  isEnabled: boolean;
  businessName: string;
  businessDescription: string;
  toneNotes: string | null;
  location: string | null;
  contactNote: string | null;
  bookingInstructions: string | null;
  preferredLanguage: string | null;
  currencyLabel: string | null;
  dailyReplyLimit: number;
  updatedAt: string | null;
  /** Ordered follow-up sequence. Empty means a single nudge after six hours. */
  followUpSteps: BotFollowUpStep[];
  /** Hours of silence before a paused chat goes back to the bot. 0 = never. */
  resumeBotAfterIdleHours: number;
  /** Team WhatsApp numbers (digits only) told when the bot hands a customer to a person. */
  handoffAlertPhones: string[];
  /** The approved three-variable template those alerts are sent with. Null = no WhatsApp alert. */
  handoffAlertTemplateId: string | null;
  /** Minutes before the bot takes a chat back after IT handed it over. 0 = only when handed back. */
  handoffResumeMinutes: number;
  /** Rules for this kind of business only, e.g. a clinic's medical rule. Null = none. */
  industryRules: string | null;
  /** Whether the bot checks times and books. Off for shops and order lines. */
  bookingsEnabled: boolean;
}

/** One follow-up step (mirrors wa_api BotFollowUpStepDto). Delay counts from the assistant's last reply. */
export interface BotFollowUpStep {
  delayMinutes: number;
  /** The owner's script — the assistant rewrites it in its own words. Used inside the 24-hour window. */
  message: string;
  /** The approved template sent once this step falls outside the window. Null means the step is skipped. */
  templateId: string | null;
  /** Fixed values from {{2}} onward. Blank entries are filled from the business details. */
  variableOverrides: string[];
}

/** One prepared answer (mirrors wa_api BotFaqResponse). */
export interface BotFaq {
  id: string;
  question: string;
  answer: string;
  sortOrder: number;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
}

/** A link the assistant may share (mirrors wa_api BotLinkResponse). */
export interface BotLink {
  id: string;
  title: string;
  url: string;
  whenToShare: string | null;
  message: string | null;
  sortOrder: number;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
}

/** One turn of a preview conversation. */
export interface BotPreviewTurn {
  isCustomer: boolean;
  text: string;
}

export interface BotPreviewToolCall {
  name: string;
  arguments: string;
  result: string;
}

/** What the assistant would have replied (mirrors wa_api BotPreviewResponse). */
export interface BotPreviewResult {
  bubbles: string[];
  outcome: string;
  toolCalls: BotPreviewToolCall[];
  inputTokens: number;
  outputTokens: number;
  latencyMs: number;
  error: string | null;
  cacheReadTokens: number;
  cacheWriteTokens: number;
  /** USD this run cost. Null when the model has no configured price on the server. */
  costUsd: number | null;
}

export const TOOL_LABELS: Record<string, string> = {
  check_availability: "Looked up free slots",
  book_appointment: "Made a booking",
  handoff_to_human: "Asked for a person",
};

/** One tool the assistant can call (mirrors wa_api BotPromptTool). */
export interface BotPromptTool {
  name: string;
  description: string;
}

/** Exactly what the model is given for this business (mirrors wa_api BotPromptResponse). Read-only. */
export interface BotPrompt {
  model: string;
  maxToolIterations: number;
  maxMessages: number;
  maxMessageLength: number;
  /** The fixed part: rules plus everything about the business (AiPromptBuilder). */
  stableSystem: string;
  /** The part rebuilt for every message: clock, dates, customer's name. */
  volatileSystem: string;
  /** The per-message part when the turn is a follow-up nudge. */
  followUpVolatileSystem: string;
  /** The separate "can I answer this?" check run before every reply (AiReplyPlanner). */
  gateInstruction: string;
  /** What the customer is sent when the chat is handed to the team. */
  holdingMessage: string;
  tools: BotPromptTool[];
}
