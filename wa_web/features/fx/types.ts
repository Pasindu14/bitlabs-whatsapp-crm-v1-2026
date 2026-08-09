/** USD→AED reference rate from GET /api/v1/fx/usd-aed. */
export interface FxRate {
  base: string;
  quote: string;
  /** Units of `quote` per 1 `base`. */
  rate: number;
  fetchedAt: string;
  /** "pegged" means the upstream provider failed and the hard-coded UAE peg was substituted. */
  source: "live" | "pegged";
}
