/**
 * The Growchat brand mark, in the two forms the app needs. Both are drawn from the same path
 * data as app/icon.svg so the tab icon, the sidebar and the sign-in panel are literally the
 * same drawing — the previous sign-in logo was a lucide MessageCircle, which drifted from
 * everything else.
 */

/** Brand green, matching the tile in app/icon.svg. Also the tick colour in every variant. */
const BRAND_GREEN = "#008236";

/**
 * Lighter tile for dark backgrounds. #008236 sits too close in value to the sign-in panel's
 * gradient (#064E3B → #047857) — the tile edge disappears and the mark reads as a loose white
 * bubble rather than a logo. This is the shade that panel already used.
 */
const BRAND_GREEN_ON_DARK = "#25D366";

/**
 * Just the double "read" ticks, no tile. Strokes in currentColor so the caller's own
 * background and text colour drive it (the sidebar tints it per theme).
 *
 * The 17x12 viewBox is wider than it is tall: size it by width and let the height follow.
 * Forcing it square squashes the ticks.
 */
export function DoubleTick({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 17 12"
      className={className}
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
    >
      <path d="M1 6.5 4.2 10 10 2.5M8 9l1 1 6.2-8" />
    </svg>
  );
}

/**
 * The full logo: green tile, white speech bubble, double tick inside. Square, so give it equal
 * height and width. The tile's rounding lives in the SVG, so use `drop-shadow-*` not `shadow-*`
 * if you want a shadow — a box-shadow would trace the element box, not the rounded tile.
 */
export function BrandMark({
  className,
  onDark = false,
}: {
  className?: string;
  /** Use the lighter tile, for placing the mark on a dark/brand-coloured background. */
  onDark?: boolean;
}) {
  return (
    <svg
      viewBox="0 0 32 32"
      className={className}
      role="img"
      aria-label="Growchat"
    >
      <rect
        width="32"
        height="32"
        rx="7"
        fill={onDark ? BRAND_GREEN_ON_DARK : BRAND_GREEN}
      />
      <rect x="6" y="8" width="20" height="13" rx="4" fill="#ffffff" />
      <path d="M11 20 L11 25.5 L16.5 20 Z" fill="#ffffff" />
      <g
        transform="translate(8.4 8.5) scale(0.94)"
        fill="none"
        stroke={BRAND_GREEN}
        strokeWidth={2.4}
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M1 6.5 4.2 10 10 2.5M8 9l1 1 6.2-8" />
      </g>
    </svg>
  );
}
