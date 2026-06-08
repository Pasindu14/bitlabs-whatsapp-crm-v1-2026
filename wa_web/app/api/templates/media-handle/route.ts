import { NextResponse } from "next/server";
import { auth } from "@/auth";
import { env } from "@/lib/env";

/**
 * Multipart proxy for template media-header samples. The builder can't post a File through the
 * JSON axios client or a server action cleanly, so it uploads here; we attach the caller's bearer
 * token and forward to wa_api POST /templates/media-handle, which talks to Meta's resumable upload.
 */
export async function POST(req: Request) {
  const session = await auth();
  const token = session?.user?.accessToken;
  if (!token) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const incoming = await req.formData();
  const file = incoming.get("file");
  if (!(file instanceof File) || file.size === 0) {
    return NextResponse.json({ error: "A file is required." }, { status: 400 });
  }

  const forward = new FormData();
  forward.append("file", file, file.name);

  let res: Response;
  try {
    res = await fetch(`${env.API_URL}/api/v1/templates/media-handle`, {
      method: "POST",
      headers: { Authorization: `Bearer ${token}` },
      body: forward,
    });
  } catch {
    return NextResponse.json({ error: "Could not reach the API." }, { status: 502 });
  }

  const json = await res.json().catch(() => null);
  if (!res.ok) {
    return NextResponse.json({ error: json?.error?.message ?? "Upload failed." }, { status: res.status });
  }

  // wa_api returns ApiResponse<MediaHandleResponse> → { data: { handle } }.
  return NextResponse.json({ handle: json?.data?.handle ?? null });
}
