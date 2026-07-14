import { NextResponse } from "next/server";
import { auth } from "@/auth";
import client, { ApiError } from "@/lib/api/client";

/**
 * Streams an inbound message's media bytes from wa_api so the inbox can render a real image with a plain
 * <img src>. Same reasoning as the template media-preview proxy: a browser <img> can't send the bearer token,
 * so we go through the shared axios `client` (dev TLS bypass + bearer interceptor) server-side and re-serve
 * the bytes same-origin. `id` is the message id; the API tenant-filters by the caller's company.
 */
export async function GET(_req: Request, { params }: { params: Promise<{ id: string }> }) {
  const session = await auth();
  if (!session?.user?.accessToken) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const { id } = await params;
  try {
    const res = await client.get(`/api/v1/media/${id}`, {
      responseType: "arraybuffer",
    });
    const contentType = (res.headers["content-type"] as string | undefined) ?? "application/octet-stream";
    return new NextResponse(Buffer.from(res.data), {
      status: 200,
      headers: {
        "Content-Type": contentType,
        // Bytes for a given message never change.
        "Cache-Control": "private, max-age=86400",
      },
    });
  } catch (e) {
    if (e instanceof ApiError) {
      return NextResponse.json({ error: e.message }, { status: e.status });
    }
    console.error("media proxy error:", e);
    return NextResponse.json({ error: "Could not reach the API." }, { status: 502 });
  }
}
