import { NextResponse } from "next/server";
import { auth } from "@/auth";
import client, { ApiError } from "@/lib/api/client";

/**
 * Streams a template header's sample-media bytes from wa_api so the builder can render a real preview.
 * Same reasoning as the media-handle proxy: we go through the shared axios `client` (dev TLS bypass +
 * bearer interceptor) and re-serve same-origin, so a plain <img src> works without leaking the token.
 */
export async function GET(_req: Request, { params }: { params: Promise<{ id: string }> }) {
  const session = await auth();
  if (!session?.user?.accessToken) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const { id } = await params;
  try {
    const res = await client.get(`/api/v1/templates/media-preview/${id}`, {
      responseType: "arraybuffer",
    });
    const contentType = (res.headers["content-type"] as string | undefined) ?? "application/octet-stream";
    return new NextResponse(Buffer.from(res.data), {
      status: 200,
      headers: {
        "Content-Type": contentType,
        // Bytes for a given id never change.
        "Cache-Control": "private, max-age=86400",
      },
    });
  } catch (e) {
    if (e instanceof ApiError) {
      return NextResponse.json({ error: e.message }, { status: e.status });
    }
    console.error("media-preview proxy error:", e);
    return NextResponse.json({ error: "Could not reach the API." }, { status: 502 });
  }
}
