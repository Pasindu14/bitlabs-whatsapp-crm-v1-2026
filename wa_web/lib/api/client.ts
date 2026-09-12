import axios from "axios";
import https from "https";
import { auth } from "@/auth";
import { env } from "@/lib/env";

// ─────────────────────────────────────────────────────────────
// API contract types — mirrors C# records in Common/Errors/ApiResponse.cs
// ─────────────────────────────────────────────────────────────

/** Shape of every successful response: ApiResponse<T> */
export interface ApiSuccessBody<T = unknown> {
  success: true;
  data: T;
  pagination: {
    page: number;
    pageSize: number;
    total: number;
    totalPages: number;
  } | null;
  traceId: string;
}

/** Shape of every error body: ApiErrorResponse */
interface ApiErrorBody {
  success: false;
  error: {
    code: string;
    message: string;
    detail: string | null;
    /** Validation field errors — each key maps to one or more messages */
    fields: Record<string, string[]> | null;
    currentData: unknown | null;
    traceId: string;
    timestamp: string;
  };
}

// ─────────────────────────────────────────────────────────────
// ApiError — thrown by the response interceptor for every non-2xx
// ─────────────────────────────────────────────────────────────

export class ApiError extends Error {
  public readonly status: number;
  public readonly code: string;
  /** Field errors flattened to Record<field, "msg1, msg2"> for form binding */
  public readonly fields?: Record<string, string>;
  public readonly detail?: string;
  public readonly currentData?: unknown;
  public readonly traceId?: string;

  constructor(
    status: number,
    code: string,
    message: string,
    fields?: Record<string, string>,
    detail?: string,
    currentData?: unknown,
    traceId?: string
  ) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
    this.fields = fields;
    this.detail = detail;
    this.currentData = currentData;
    this.traceId = traceId;
    if (Error.captureStackTrace) {
      Error.captureStackTrace(this, this.constructor);
    }
  }
}

// ─────────────────────────────────────────────────────────────
// Idempotency key helper
// Callers generate a stable key BEFORE initiating a mutation and pass it
// via `config.headers["X-Idempotency-Key"]`.  The interceptor no longer
// sets a key globally — a per-request UUID defeats the purpose.
// ─────────────────────────────────────────────────────────────

/**
 * Generate a stable idempotency key for a mutation.
 * Call this once before the mutation and pass the result in the request
 * headers so that retries of the same logical operation reuse the key.
 *
 * @example
 * const idempotencyKey = createIdempotencyKey();
 * await client.post('/api/v1/orders', body, {
 *   headers: { 'X-Idempotency-Key': idempotencyKey },
 * });
 */
export function createIdempotencyKey(): string {
  return crypto.randomUUID();
}

// ─────────────────────────────────────────────────────────────
// Axios client
// ─────────────────────────────────────────────────────────────

const client = axios.create({
  baseURL: env.API_URL,
  headers: { "Content-Type": "application/json" },
  timeout: 30_000,
  httpsAgent: new https.Agent({
    rejectUnauthorized: process.env.NODE_ENV === "production",
  }),
});

// Read-only guard for marketing screenshot capture.
// Set CAPTURE_READONLY=1 when running the dev server for `npm run test:capture` so a stray
// click can never write to a live tenant (send a WhatsApp message, launch a campaign, submit a
// template to Meta). Every server action reaches the API through this client, so refusing
// non-GET here is the single choke point. Inert unless the flag is explicitly set.
const CAPTURE_READONLY = process.env.CAPTURE_READONLY === "1";

client.interceptors.request.use((config) => {
  if (!CAPTURE_READONLY) return config;

  const method = (config.method ?? "get").toUpperCase();
  if (method === "GET" || method === "HEAD") return config;

  throw new ApiError(
    403,
    "CAPTURE_READONLY",
    `Blocked ${method} ${config.url} — the server is running in CAPTURE_READONLY mode.`
  );
});

// Attach Bearer token from Next-Auth session on every request
client.interceptors.request.use(async (config) => {
  const session = await auth();
  if (session?.user?.accessToken) {
    config.headers.Authorization = `Bearer ${session.user.accessToken}`;
  }

  return config;
});

// Normalise all non-2xx responses into ApiError
// For 401 responses with RefreshAccessTokenError, the session error field
// signals that the refresh failed and the user must re-authenticate.
// Client-side components should check `session.error === "RefreshAccessTokenError"`
// and call `signOut()` from `next-auth/react` to redirect to login.
client.interceptors.response.use(
  (response) => response,
  async (error) => {
    if (axios.isAxiosError(error) && error.response) {
      const status = error.response.status;
      const body = error.response.data as ApiErrorBody | undefined;
      const apiErr = body?.error;

      // Fallback message when the body is not our standard envelope
      const message =
        apiErr?.message ??
        error.message ??
        "An unexpected error occurred";

      const code =
        apiErr?.code ??
        (status === 401
          ? "UNAUTHORIZED"
          : status === 403
          ? "FORBIDDEN_ACCESS"
          : status === 404
          ? "NOT_FOUND"
          : status === 409
          ? "CONFLICT"
          : status === 422
          ? "BUSINESS_RULE"
          : status === 429
          ? "RATE_LIMITED"
          : status === 503
          ? "SERVICE_UNAVAILABLE"
          : "INTERNAL_ERROR");

      // If token refresh failed, the session carries a RefreshAccessTokenError.
      // The client-side SessionGuard / layout should detect this and call signOut().
      // We surface it here as a specific error code so callers can differentiate
      // an expired-and-unrefreshable token from a plain authorization failure.
      if (status === 401 && apiErr?.code === "AUTH_TOKEN_EXPIRED") {
        throw new ApiError(
          status,
          "AUTH_TOKEN_EXPIRED",
          message,
          undefined,
          apiErr?.detail ?? undefined,
          apiErr?.currentData ?? undefined,
          apiErr?.traceId
        );
      }

      // Flatten fields and convert PascalCase keys → camelCase to match form field names
      // e.g. { Name: ["msg1", "msg2"] } → { name: "msg1, msg2" }
      const fields: Record<string, string> | undefined =
        apiErr?.fields && Object.keys(apiErr.fields).length > 0
          ? Object.fromEntries(
              Object.entries(apiErr.fields).map(([k, v]) => [
                k.charAt(0).toLowerCase() + k.slice(1),
                Array.isArray(v) ? v.join(", ") : String(v),
              ])
            )
          : undefined;

      throw new ApiError(
        status,
        code,
        message,
        fields,
        apiErr?.detail ?? undefined,
        apiErr?.currentData ?? undefined,
        apiErr?.traceId
      );
    }
    throw error;
  }
);

export default client;
