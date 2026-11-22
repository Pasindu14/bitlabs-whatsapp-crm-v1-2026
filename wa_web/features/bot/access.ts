/**
 * Shared by the server-side service and the client-side hook, so this file must stay free of
 * server-only imports (the API client pulls in lib/env, which cannot load in a browser).
 */

/** Mirrors wa_api BotAccessResponse. */
export interface BotAccess {
  /** The platform master switch. Off means the bot answers nobody, whatever the plan says. */
  platformEnabled: boolean;
  /** The caller's plan includes the chatbot. */
  chatbot: boolean;
  /** The caller's plan includes the calendar. */
  calendar: boolean;
}

export const NO_BOT_ACCESS: BotAccess = { platformEnabled: false, chatbot: false, calendar: false };
