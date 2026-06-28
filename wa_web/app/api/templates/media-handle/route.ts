import { NextResponse } from "next/server";
import NodeFormData from "form-data";
import { auth } from "@/auth";
import client, { ApiError } from "@/lib/api/client";

/**
 * Multipart proxy for template media-header samples. The builder can't post a File through a server
 * action cleanly, so it uploads here and we forward to wa_api POST /templates/media-handle.
 *
 * We forward through the shared axios `client` (not a raw fetch) on purpose: it already carries the
 * dev TLS bypass (rejectUnauthorized:false outside prod) + the bearer interceptor. A raw fetch to the
 * .NET dev server's self-signed HTTPS cert throws ("Could not reach the API."), since native fetch
 * has no per-request agent/cert option that reliably works.
 */
export async function POST(req: Request) {
  const session = await auth();
  if (!session?.user?.accessToken) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const incoming = await req.formData();
  const file = incoming.get("file");
  if (!(file instanceof File) || file.size === 0) {
    return NextResponse.json({ error: "A file is required." }, { status: 400 });
  }

  // axios in Node serializes the `form-data` package cleanly; a Buffer keeps the file in memory
  // (sample media is small) and lets form-data set the multipart boundary + content-type.
  const buffer = Buffer.from(await file.arrayBuffer());
  const forward = new NodeFormData();
  forward.append("file", buffer, {
    filename: file.name,
    contentType: file.type || "application/octet-stream",
  });

  try {
    // The client's request interceptor attaches the bearer token from the session.
    const res = await client.post("/api/v1/templates/media-handle", forward, {
      headers: forward.getHeaders(),
    });
    // wa_api returns ApiResponse<MediaHandleResponse> → { data: { handle, previewId } }.
    return NextResponse.json({
      handle: res.data?.data?.handle ?? null,
      previewId: res.data?.data?.previewId ?? null,
    });
  } catch (e) {
    // The interceptor turns every non-2xx into an ApiError (carries Meta's reason for MEDIA_UPLOAD_FAILED).
    if (e instanceof ApiError) {
      return NextResponse.json({ error: e.message }, { status: e.status });
    }
    console.error("media-handle proxy error:", e);
    return NextResponse.json({ error: "Could not reach the API." }, { status: 502 });
  }
}
