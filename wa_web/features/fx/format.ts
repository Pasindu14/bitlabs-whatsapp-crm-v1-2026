const AED_FMT = new Intl.NumberFormat("en-AE", {
  style: "currency",
  currency: "AED",
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

/**
 * Indicative dirham equivalent of a plan price, e.g. `"≈ AED 106.50"`.
 *
 * Returns null — meaning "render the native price alone" — unless the plan is genuinely USD and a
 * rate is loaded. The currency guard matters: plans priced in AED already display in dirhams, and
 * multiplying those by the rate would show a number roughly 3.7× the real price.
 */
export function formatAedApprox(
  price: number,
  currency: string,
  rate: number | undefined
): string | null {
  if (!rate || currency?.toUpperCase() !== "USD") return null;
  return `≈ ${AED_FMT.format(price * rate)}`;
}
