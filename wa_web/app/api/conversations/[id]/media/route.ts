import { NextResponse } from "next/server";
import NodeFormData from "form-data";
import { auth } from "@/auth";
import client, { ApiError } from "@/lib/api/client";
import { MAX_ATTACHMENT_BYTES } from "@/features/conversations/attachments";

/**
 * Multipart proxy for sending a file from the inbox. Same pattern as the template media-handle proxy: the
 * browser can't carry the bearer token to wa_api, so it posts here and we forward through the shared axios
 * `client`. Vercel rejects request bodies over ~4.5 MB before this code runs, so the size is checked here too
 * for a clear message on other hosts.
 */
export async function POST(req: Request, { params }: { params: Promise<{ id: string }> }) {
  const session = await auth();
  if (!session?.user?.accessToken) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const { id } = await params;
  const incoming = await req.formData();
  const file = incoming.get("file");
  const caption = incoming.get("caption");

  if (!(file instanceof File) || file.size === 0) {
    return NextResponse.json({ error: "Choose a file to send." }, { status: 400 });
  }
  if (file.size > MAX_ATTACHMENT_BYTES) {
    return NextResponse.json(
      { error: `Files up to ${MAX_ATTACHMENT_BYTES / (1024 * 1024)} MB can be sent from here.` },
      { status: 413 },
    );
  }

  const forward = new NodeFormData();
  forward.append("file", Buffer.from(await file.arrayBuffer()), {
    filename: file.name,
    contentType: file.type || "application/octet-stream",
  });
  if (typeof caption === "string" && caption.trim()) forward.append("caption", caption.trim());

  try {
    const res = await client.post(`/api/v1/conversations/${id}/messages/media`, forward, {
      headers: forward.getHeaders(),
    });
    return NextResponse.json({ message: res.data?.data ?? null });
  } catch (e) {
    if (e instanceof ApiError) {
      return NextResponse.json({ error: e.message }, { status: e.status });
    }
    console.error("send-media proxy error:", e);
    return NextResponse.json({ error: "Could not reach the API." }, { status: 502 });
  }
}
