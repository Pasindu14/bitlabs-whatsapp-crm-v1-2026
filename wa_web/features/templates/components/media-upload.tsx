"use client";

import { useRef, useState } from "react";
import { Upload, X } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";

const ACCEPT: Record<string, string> = {
  image: "image/jpeg,image/png",
  document: "application/pdf",
};

interface MediaUploadProps {
  mediaType: "image" | "document";
  /** Current handle ("" when none). */
  value: string;
  onChange: (handle: string) => void;
  disabled?: boolean;
}

export function MediaUpload({ mediaType, value, onChange, disabled }: MediaUploadProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const [fileName, setFileName] = useState<string | null>(null);

  async function handleFile(file: File) {
    setUploading(true);
    try {
      const fd = new FormData();
      fd.append("file", file);
      const res = await fetch("/api/templates/media-handle", { method: "POST", body: fd });
      const json = await res.json().catch(() => null);
      if (!res.ok || !json?.handle) throw new Error(json?.error ?? "Upload failed");
      onChange(json.handle);
      setFileName(file.name);
      toast.success("Sample media uploaded");
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Upload failed");
    } finally {
      setUploading(false);
      if (inputRef.current) inputRef.current.value = "";
    }
  }

  return (
    <div className="space-y-2">
      <input
        ref={inputRef}
        type="file"
        accept={ACCEPT[mediaType]}
        className="hidden"
        onChange={(e) => {
          const f = e.target.files?.[0];
          if (f) handleFile(f);
        }}
      />
      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disabled || uploading}
          onClick={() => inputRef.current?.click()}
        >
          {uploading ? <Spinner className="mr-2 size-4" /> : <Upload className="mr-2 h-4 w-4" />}
          {value ? "Replace sample" : "Upload sample"}
        </Button>
        {value && (
          <span className="flex items-center gap-1 text-xs text-emerald-600 dark:text-emerald-400">
            {fileName ?? "Sample attached"}
            <button
              type="button"
              onClick={() => {
                onChange("");
                setFileName(null);
              }}
              className="text-muted-foreground hover:text-foreground"
              aria-label="Remove sample"
            >
              <X className="h-3 w-3" />
            </button>
          </span>
        )}
      </div>
      <p className="text-xs text-muted-foreground">
        Meta needs a sample {mediaType} to approve the header. (Requires Meta app credentials configured on the server.)
      </p>
    </div>
  );
}
