"use client";

import { formatDistanceToNow } from "date-fns";
import {
  Wifi,
  WifiOff,
  AlertCircle,
  Phone,
  ShieldCheck,
  ShieldX,
  Clock,
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";
import { useConnections } from "@/features/connections/hooks/use-connections";
import type { MyWabaConnection, WabaConnectionStatus } from "@/features/connections/types";

function StatusBadge({ status }: { status: WabaConnectionStatus }) {
  if (status === "Connected")
    return (
      <Badge className="gap-1 bg-green-100 text-green-700 hover:bg-green-100 border-green-200">
        <Wifi className="size-3" />
        Connected
      </Badge>
    );
  if (status === "Invalid")
    return (
      <Badge className="gap-1 bg-red-100 text-red-700 hover:bg-red-100 border-red-200">
        <AlertCircle className="size-3" />
        Invalid
      </Badge>
    );
  return (
    <Badge className="gap-1 bg-zinc-100 text-zinc-600 hover:bg-zinc-100 border-zinc-200">
      <WifiOff className="size-3" />
      Disconnected
    </Badge>
  );
}

function ConnectionCard({ conn }: { conn: MyWabaConnection }) {
  return (
    <div className="rounded-xl border bg-card p-5 space-y-4">
      <div className="flex items-start justify-between gap-3">
        <div className="flex items-center gap-3">
          <div className="flex size-10 shrink-0 items-center justify-center rounded-full bg-muted">
            <Phone className="size-5 text-muted-foreground" />
          </div>
          <div>
            <p className="font-semibold text-base">{conn.displayPhoneNumber}</p>
            <p className="text-xs text-muted-foreground">WABA ID: {conn.wabaId}</p>
          </div>
        </div>
        <StatusBadge status={conn.status} />
      </div>

      <div className="grid grid-cols-2 gap-x-6 gap-y-2 text-sm">
        <div>
          <p className="text-xs text-muted-foreground">Phone Number ID</p>
          <p className="font-mono text-xs mt-0.5 truncate">{conn.phoneNumberId}</p>
        </div>
        <div>
          <p className="text-xs text-muted-foreground">Access Token</p>
          <div className="flex items-center gap-1 mt-0.5">
            {conn.hasAccessToken ? (
              <>
                <ShieldCheck className="size-3.5 text-green-600" />
                <span className="text-xs text-green-700">Configured</span>
              </>
            ) : (
              <>
                <ShieldX className="size-3.5 text-red-500" />
                <span className="text-xs text-red-600">Missing</span>
              </>
            )}
          </div>
        </div>
        {conn.lastHealthCheckAt && (
          <div className="col-span-2">
            <p className="text-xs text-muted-foreground">Last health check</p>
            <div className="flex items-center gap-1 mt-0.5">
              <Clock className="size-3 text-muted-foreground" />
              <span className="text-xs">
                {formatDistanceToNow(new Date(conn.lastHealthCheckAt), { addSuffix: true })}
              </span>
            </div>
          </div>
        )}
        {conn.healthCheckErrorMessage && (
          <div className="col-span-2">
            <p className="text-xs text-muted-foreground">Error</p>
            <p className="text-xs text-red-600 mt-0.5">{conn.healthCheckErrorMessage}</p>
          </div>
        )}
      </div>
    </div>
  );
}

function ConnectionSkeleton() {
  return (
    <div className="rounded-xl border bg-card p-5 space-y-4">
      <div className="flex items-start justify-between gap-3">
        <div className="flex items-center gap-3">
          <Skeleton className="size-10 rounded-full" />
          <div className="space-y-2">
            <Skeleton className="h-4 w-36" />
            <Skeleton className="h-3 w-28" />
          </div>
        </div>
        <Skeleton className="h-5 w-24 rounded-full" />
      </div>
      <div className="grid grid-cols-2 gap-4">
        <Skeleton className="h-8" />
        <Skeleton className="h-8" />
      </div>
    </div>
  );
}

export function ConnectionsView() {
  const { data, isLoading } = useConnections();

  return (
    <div className="space-y-4">
      {isLoading ? (
        <div className="grid gap-4 sm:grid-cols-2">
          {Array.from({ length: 2 }).map((_, i) => (
            <ConnectionSkeleton key={i} />
          ))}
        </div>
      ) : !data?.length ? (
        <div className="flex flex-col items-center justify-center gap-3 rounded-xl border border-dashed py-20 text-center">
          <WifiOff className="size-10 text-muted-foreground/40" />
          <div>
            <p className="text-sm font-medium">No connections</p>
            <p className="mt-1 text-xs text-muted-foreground">
              Contact your administrator to set up a WhatsApp Business connection.
            </p>
          </div>
        </div>
      ) : (
        <>
          <p className="text-sm text-muted-foreground">
            {data.length} connection{data.length !== 1 ? "s" : ""}
          </p>
          <div className={cn("grid gap-4", data.length > 1 && "sm:grid-cols-2")}>
            {data.map((conn) => (
              <ConnectionCard key={conn.id} conn={conn} />
            ))}
          </div>
        </>
      )}
    </div>
  );
}
