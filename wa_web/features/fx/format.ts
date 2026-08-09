const AED_FMT = new Intl.NumberFormat("en-AE", {
  style: "currency",
  currency: "AED",
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const USD_FMT = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

export interface DirhamFirstPrice {
  /** Headline figure, e.g. `"AED 106.50"`. Derived from the USD price — indicative, not charged. */
  aed: string;
  /** The exact amount the customer's card is charged, e.g. `"$29.00"`. */
  usd: string;
}

/**
 * Dirham-led presentation of a plan price: dirhams as the headline for the UAE audience, with the
 * real USD charge shown beneath it.
 *
 * Returns null — meaning "render the plan's native price alone" — unless the plan is genuinely USD
 * and a rate is loaded. The currency guard is what keeps the labelling honest: for a plan already
 * priced in AED, USD is not the charge currency, so calling a converted figure "charged in USD"
 * would be a lie, and multiplying its price by the rate would inflate it roughly 3.7×.
 */
export function formatDirhamFirst(
  price: number,
  currency: string,
  rate: number | undefined
): DirhamFirstPrice | null {
  if (!rate || currency?.toUpperCase() !== "USD") return null;
  return { aed: AED_FMT.format(price * rate), usd: USD_FMT.format(price) };
}
