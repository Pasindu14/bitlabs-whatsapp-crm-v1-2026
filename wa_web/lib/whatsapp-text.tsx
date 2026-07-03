import React, { type ReactNode } from "react";

/**
 * Renders WhatsApp's text markup the way the recipient's phone shows it, so our template
 * preview and inbox match the real message. Supports the core four formats:
 *
 *   *bold*        → bold
 *   _italic_      → italic
 *   ~strike~      → strikethrough
 *   ```mono```    → monospace (content is literal — not parsed for nested markup)
 *
 * Parsing is done into React nodes (never dangerouslySetInnerHTML), so message text can't
 * inject markup. Unmatched markers are left as plain characters, matching WhatsApp.
 */
type Rule = {
  open: string;
  close: string;
  recurse: boolean;
  render: (children: ReactNode, key: string) => ReactNode;
};

// Order matters: the multi-char monospace fence must be tried before the single-char markers.
const RULES: Rule[] = [
  { open: "```", close: "```", recurse: false, render: (c, k) => <code key={k} className="font-mono">{c}</code> },
  { open: "*", close: "*", recurse: true, render: (c, k) => <strong key={k}>{c}</strong> },
  { open: "_", close: "_", recurse: true, render: (c, k) => <em key={k}>{c}</em> },
  { open: "~", close: "~", recurse: true, render: (c, k) => <s key={k}>{c}</s> },
];

function parse(text: string, keyPrefix: string): ReactNode[] {
  const nodes: ReactNode[] = [];
  let plainStart = 0;
  let i = 0;
  let keyN = 0;

  const flushPlain = (end: number) => {
    if (end > plainStart) nodes.push(text.slice(plainStart, end));
  };

  while (i < text.length) {
    let matched = false;
    for (const rule of RULES) {
      if (!text.startsWith(rule.open, i)) continue;
      const contentStart = i + rule.open.length;
      const closeIdx = text.indexOf(rule.close, contentStart);
      // Require non-empty content between the markers, mirroring WhatsApp (it won't format "**").
      if (closeIdx <= contentStart) continue;

      flushPlain(i);
      const key = `${keyPrefix}-${keyN++}`;
      const inner = text.slice(contentStart, closeIdx);
      nodes.push(rule.render(rule.recurse ? parse(inner, key) : inner, key));
      i = closeIdx + rule.close.length;
      plainStart = i;
      matched = true;
      break;
    }
    if (!matched) i++;
  }

  flushPlain(text.length);
  return nodes;
}

/** Convert a WhatsApp-formatted string to React nodes. Safe for empty/undefined input. */
export function renderWhatsAppText(text: string | null | undefined): ReactNode {
  if (!text) return text ?? null;
  return parse(text, "wa");
}
