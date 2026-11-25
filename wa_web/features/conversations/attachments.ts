/**
 * What the inbox lets an agent attach. Mirrors wa_api's OutboundMediaRules (the API is the authority and
 * re-checks everything); this only gives instant feedback before a file is uploaded.
 */

/**
 * Vercel caps a serverless request body at ~4.5 MB, so the proxy accepts 4 MB. Raising it needs a direct
 * browser → API upload; the API itself takes Meta's full limits (image 5 MB, audio/video 16 MB, docs 100 MB).
 */
export const MAX_ATTACHMENT_BYTES = 4 * 1024 * 1024;

export const ATTACHMENT_ACCEPT = [
  "image/jpeg",
  "image/png",
  "audio/mpeg",
  "audio/aac",
  "audio/mp4",
  "audio/amr",
  "audio/ogg",
  "video/mp4",
  "video/3gpp",
  "application/pdf",
  "application/msword",
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
  "application/vnd.ms-excel",
  "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
  "application/vnd.ms-powerpoint",
  "application/vnd.openxmlformats-officedocument.presentationml.presentation",
  "text/plain",
];

export type AttachmentKind = "image" | "audio" | "video" | "document";

export function attachmentKind(mime: string): AttachmentKind | null {
  const type = mime.split(";")[0].trim().toLowerCase();
  if (!ATTACHMENT_ACCEPT.includes(type)) return null;
  if (type.startsWith("image/")) return "image";
  if (type.startsWith("audio/")) return "audio";
  if (type.startsWith("video/")) return "video";
  return "document";
}

/** Returns a problem to show the agent, or null when the file can be sent. */
export function validateAttachment(file: File): string | null {
  if (!attachmentKind(file.type)) {
    return "WhatsApp can't send this kind of file. Use a JPG/PNG photo, MP3/AAC/OGG audio, MP4 video, or a PDF, Word, Excel, PowerPoint or text file.";
  }
  if (file.size > MAX_ATTACHMENT_BYTES) {
    return `This file is too large. You can send files up to ${MAX_ATTACHMENT_BYTES / (1024 * 1024)} MB.`;
  }
  return null;
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
