"use client";

import { useState } from "react";
import { format } from "date-fns";
import { AlertTriangle, PlugZap, Unplug } from "lucide-react";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Spinner } from "@/components/ui/spinner";
import { Switch } from "@/components/ui/switch";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import {
  useClinicCrm,
  useClinicCrmTreatments,
  useDeleteClinicCrm,
  useSaveClinicCrm,
  useSaveClinicCrmMappings,
  useTestClinicCrm,
} from "@/features/clinic-crm/hooks/use-clinic-crm";
import type { ClinicCrmSettings } from "@/features/clinic-crm/types";

/** A Select item cannot have an empty value, so "nothing picked" needs a stand-in. */
const NO_TREATMENT = "__none__";

/**
 * The link to the clinic's own CRM (plan 014). While it is on, the clinic's CRM is the calendar that
 * counts: the assistant offers the clinic's free times, books there first, and only then shows the
 * booking here.
 */
export function ClinicCrmSettingsView() {
  const { data: settings, isLoading } = useClinicCrm();

  if (isLoading || !settings) {
    return (
      <div className="space-y-6">
        <Skeleton className="h-72 w-full rounded-xl" />
        <Skeleton className="h-48 w-full rounded-xl" />
      </div>
    );
  }

  // Keyed on what was saved, so a save or a disconnect remounts each form with the new values
  // rather than syncing local state in an effect.
  const connectionKey = [settings.configured, settings.baseUrl, settings.clientId, settings.hasSecret, settings.isEnabled, settings.notifyOnMessages].join("|");
  const mappingKey = settings.services.map((s) => `${s.appointmentTypeId}:${s.externalTreatmentId ?? ""}`).join("|");

  return (
    <div className="space-y-6">
      <ConnectionCard key={connectionKey} settings={settings} />
      {settings.configured ? <TreatmentMatchingCard key={mappingKey} settings={settings} /> : null}
    </div>
  );
}

function formatWhen(iso: string | null) {
  return iso ? format(new Date(iso), "d MMM yyyy, HH:mm") : null;
}

function StatusBadge({ settings }: { settings: ClinicCrmSettings }) {
  if (!settings.configured) return <Badge variant="outline">Not connected</Badge>;
  if (!settings.isEnabled) return <Badge variant="secondary">Switched off</Badge>;
  if (settings.lastError) return <Badge variant="destructive">Problem</Badge>;
  if (settings.lastSuccessAt) return <Badge>Connected</Badge>;
  return <Badge variant="secondary">Not tested yet</Badge>;
}

function ConnectionCard({ settings }: { settings: ClinicCrmSettings }) {
  const save = useSaveClinicCrm();
  const test = useTestClinicCrm();
  const remove = useDeleteClinicCrm();

  const [baseUrl, setBaseUrl] = useState(settings.baseUrl ?? "");
  const [clientId, setClientId] = useState(settings.clientId ?? "");
  const [clientSecret, setClientSecret] = useState("");
  const [isEnabled, setIsEnabled] = useState(settings.configured ? settings.isEnabled : true);
  const [notifyOnMessages, setNotifyOnMessages] = useState(settings.configured ? settings.notifyOnMessages : false);
  const [confirmDisconnect, setConfirmDisconnect] = useState(false);

  const needsSecret = !settings.hasSecret;
  const canSave =
    baseUrl.trim().length > 0 &&
    clientId.trim().length > 0 &&
    (!needsSecret || clientSecret.trim().length > 0) &&
    !save.isPending;

  const plainHttp = baseUrl.trim().toLowerCase().startsWith("http://");

  function submit() {
    if (!canSave) return;
    save.mutate({
      baseUrl: baseUrl.trim(),
      clientId: clientId.trim(),
      clientSecret: clientSecret.trim() || null,
      isEnabled,
      notifyOnMessages,
    });
  }

  return (
    <Card>
      <CardHeader className="flex flex-row items-start justify-between gap-4 space-y-0">
        <div className="space-y-1.5">
          <CardTitle>Clinic CRM</CardTitle>
          <CardDescription>
            Send the assistant&apos;s bookings straight into the clinic&apos;s CRM. While this is on, the
            assistant offers the clinic&apos;s own free times and a customer is only told they are booked
            once the clinic&apos;s system has accepted it. Get these details from the clinic&apos;s admin
            (Administration → WhatsApp BOT → Connections).
          </CardDescription>
        </div>
        <StatusBadge settings={settings} />
      </CardHeader>

      <CardContent className="space-y-5">
        <div className="grid gap-4 md:grid-cols-2">
          <div className="space-y-2 md:col-span-2">
            <Label htmlFor="crm-base-url">API address</Label>
            <Input
              id="crm-base-url"
              placeholder="http://169.58.92.105:3110/api/bot"
              value={baseUrl}
              onChange={(e) => setBaseUrl(e.target.value)}
              aria-invalid={!!save.fieldErrors?.baseUrl}
            />
            {save.fieldErrors?.baseUrl ? (
              <p className="text-sm text-destructive">{save.fieldErrors.baseUrl}</p>
            ) : null}
          </div>

          <div className="space-y-2">
            <Label htmlFor="crm-client-id">Client ID</Label>
            <Input
              id="crm-client-id"
              placeholder="gdc_…"
              value={clientId}
              onChange={(e) => setClientId(e.target.value)}
              aria-invalid={!!save.fieldErrors?.clientId}
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="crm-client-secret">Client secret</Label>
            <Input
              id="crm-client-secret"
              type="password"
              autoComplete="off"
              placeholder={settings.hasSecret ? "Saved — leave blank to keep it" : "gds_…"}
              value={clientSecret}
              onChange={(e) => setClientSecret(e.target.value)}
            />
          </div>
        </div>

        <div className="flex items-center justify-between gap-4 rounded-lg border px-4 py-3">
          <div className="space-y-0.5">
            <Label htmlFor="crm-enabled">Book into the clinic CRM</Label>
            <p className="text-sm text-muted-foreground">
              Off keeps the details saved but books on this calendar instead.
            </p>
          </div>
          <Switch id="crm-enabled" checked={isEnabled} onCheckedChange={setIsEnabled} />
        </div>

        <div className="flex items-center justify-between gap-4 rounded-lg border px-4 py-3">
          <div className="space-y-0.5">
            <Label htmlFor="crm-notify-messages" className={isEnabled ? undefined : "text-muted-foreground"}>
              Notify the CRM about new messages
            </Label>
            <p className="text-sm text-muted-foreground">
              Tells the clinic&apos;s CRM when a customer writes in with a real message. STOP, reactions,
              stickers and a plain &ldquo;ok&rdquo; or &ldquo;thanks&rdquo; are not sent.
            </p>
          </div>
          <Switch
            id="crm-notify-messages"
            checked={isEnabled && notifyOnMessages}
            disabled={!isEnabled}
            onCheckedChange={setNotifyOnMessages}
          />
        </div>

        {plainHttp ? (
          <Alert>
            <AlertTriangle className="size-4" />
            <AlertTitle>This address is not encrypted</AlertTitle>
            <AlertDescription>
              With http:// the client secret and customers&apos; names and numbers travel unencrypted. Ask the
              clinic for an https:// address before real customers use it.
            </AlertDescription>
          </Alert>
        ) : null}

        {settings.configured && (settings.lastSuccessAt || settings.lastError) ? (
          <div className="space-y-1 text-sm">
            {settings.lastSuccessAt ? (
              <p className="text-muted-foreground">Last worked {formatWhen(settings.lastSuccessAt)}.</p>
            ) : null}
            {settings.lastError ? (
              <p className="text-destructive">
                {settings.lastError} ({formatWhen(settings.lastErrorAt)})
              </p>
            ) : null}
          </div>
        ) : null}

        <div className="flex flex-wrap items-center gap-2">
          <Button type="button" onClick={submit} disabled={!canSave}>
            {save.isPending ? <Spinner className="mr-2 size-4" /> : null}
            Save
          </Button>
          <Button
            type="button"
            variant="outline"
            onClick={() => test.mutate()}
            disabled={!settings.configured || test.isPending}
          >
            {test.isPending ? <Spinner className="mr-2 size-4" /> : <PlugZap className="mr-2 size-4" />}
            Test connection
          </Button>
          {settings.configured ? (
            <Button
              type="button"
              variant="ghost"
              className="text-destructive hover:text-destructive"
              onClick={() => setConfirmDisconnect(true)}
            >
              <Unplug className="mr-2 size-4" />
              Disconnect
            </Button>
          ) : null}
        </div>
      </CardContent>

      <AlertDialog open={confirmDisconnect} onOpenChange={setConfirmDisconnect}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Disconnect the clinic CRM?</AlertDialogTitle>
            <AlertDialogDescription>
              The assistant goes back to booking on this calendar. Bookings already in the clinic&apos;s CRM
              stay there, and the saved secret is deleted.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                e.preventDefault();
                remove.mutate(undefined, { onSuccess: () => setConfirmDisconnect(false) });
              }}
            >
              {remove.isPending ? <Spinner className="mr-2 size-4" /> : null}
              Disconnect
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Card>
  );
}

/**
 * Which clinic treatment each of our services books as. Names are never matched automatically — a
 * service left unmatched still books, with no treatment, and the clinic fills it in.
 */
function TreatmentMatchingCard({ settings }: { settings: ClinicCrmSettings }) {
  const treatments = useClinicCrmTreatments(settings.configured && settings.hasSecret);
  const saveMappings = useSaveClinicCrmMappings();

  const [draft, setDraft] = useState<Record<string, number | null>>(() =>
    Object.fromEntries(settings.services.map((s) => [s.appointmentTypeId, s.externalTreatmentId]))
  );

  const dirty = settings.services.some((s) => (draft[s.appointmentTypeId] ?? null) !== s.externalTreatmentId);

  function submit() {
    saveMappings.mutate({
      items: settings.services.map((s) => ({
        appointmentTypeId: s.appointmentTypeId,
        externalTreatmentId: draft[s.appointmentTypeId] ?? null,
      })),
    });
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Match your services to the clinic&apos;s treatments</CardTitle>
        <CardDescription>
          Each service the assistant books points at one of the clinic&apos;s treatments. A service left
          unmatched is still booked, with no treatment, for the clinic to fill in.
        </CardDescription>
      </CardHeader>

      <CardContent className="space-y-4">
        {treatments.isError ? (
          <Alert variant="destructive">
            <AlertTriangle className="size-4" />
            <AlertTitle>Could not load the clinic&apos;s treatments</AlertTitle>
            <AlertDescription>
              {treatments.error instanceof Error ? treatments.error.message : "Check the connection details."}
            </AlertDescription>
          </Alert>
        ) : null}

        {settings.services.length === 0 ? (
          <p className="rounded-lg border border-dashed px-3 py-8 text-center text-sm text-muted-foreground">
            You have no services yet. Add them under Calendar → Settings first.
          </p>
        ) : (
          <div className="divide-y rounded-lg border">
            {settings.services.map((service) => (
              <div
                key={service.appointmentTypeId}
                className="flex flex-col gap-2 px-4 py-3 sm:flex-row sm:items-center sm:justify-between"
              >
                <p className={`text-sm font-medium ${service.isEnabled ? "" : "text-muted-foreground"}`}>
                  {service.name}
                  {service.isEnabled ? null : <span className="ml-2 text-xs font-normal">(switched off)</span>}
                </p>
                {treatments.isLoading ? (
                  <Skeleton className="h-9 w-64" />
                ) : (
                  <Select
                    value={draft[service.appointmentTypeId]?.toString() ?? NO_TREATMENT}
                    disabled={!treatments.data}
                    onValueChange={(v) =>
                      setDraft((d) => ({
                        ...d,
                        [service.appointmentTypeId]: v === NO_TREATMENT ? null : Number(v),
                      }))
                    }
                  >
                    <SelectTrigger className="w-full sm:w-64">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={NO_TREATMENT}>No treatment</SelectItem>
                      {(treatments.data ?? []).map((t) => (
                        <SelectItem key={t.id} value={t.id.toString()}>
                          {t.name}
                        </SelectItem>
                      ))}
                      {/* A saved match the clinic has since removed stays visible, so it can be changed. */}
                      {draft[service.appointmentTypeId] != null &&
                      treatments.data &&
                      !treatments.data.some((t) => t.id === draft[service.appointmentTypeId]) ? (
                        <SelectItem value={draft[service.appointmentTypeId]!.toString()}>
                          Treatment #{draft[service.appointmentTypeId]} (no longer at the clinic)
                        </SelectItem>
                      ) : null}
                    </SelectContent>
                  </Select>
                )}
              </div>
            ))}
          </div>
        )}

        <Button type="button" onClick={submit} disabled={!dirty || saveMappings.isPending}>
          {saveMappings.isPending ? <Spinner className="mr-2 size-4" /> : null}
          Save matches
        </Button>
      </CardContent>
    </Card>
  );
}
