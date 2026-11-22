"use client";

import { useEffect, useState } from "react";
import { useForm, type Resolver } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Spinner } from "@/components/ui/spinner";
import { Skeleton } from "@/components/ui/skeleton";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
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
import { cn } from "@/lib/utils";
import {
  useAppointmentTypes,
  useDeleteAppointmentType,
  useSaveAppointmentType,
} from "@/features/calendar/hooks/use-calendar";
import { useTypeFormDialog } from "@/features/calendar/store/calendar-store";
import {
  appointmentTypeSchema,
  type AppointmentTypeInput,
} from "@/features/calendar/schema/calendar-schema";
import type { AppointmentType } from "@/features/calendar/types";

const DEFAULTS: AppointmentTypeInput = {
  name: "",
  description: "",
  durationMinutes: 30,
  slotIntervalMinutes: 30,
  bufferBeforeMinutes: 0,
  bufferAfterMinutes: 0,
  minimumNoticeMinutes: 60,
  bookingHorizonDays: 30,
  isEnabled: true,
};

function TypeFormDialog({ types }: { types: AppointmentType[] }) {
  const { isOpen, selectedId, close } = useTypeFormDialog();
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useSaveAppointmentType();

  const existing = types.find((t) => t.id === selectedId);

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<AppointmentTypeInput>({
    // Cast for the same reason as RuleForm: z.coerce.number() widens the schema's INPUT type to
    // unknown (it accepts the string an <input type="number"> yields), which no longer matches the
    // parsed output type the form is generic over.
    resolver: zodResolver(appointmentTypeSchema) as Resolver<AppointmentTypeInput>,
    defaultValues: DEFAULTS,
  });

  useEffect(() => {
    if (!isOpen) return;
    reset(
      existing
        ? {
            name: existing.name,
            description: existing.description ?? "",
            durationMinutes: existing.durationMinutes,
            slotIntervalMinutes: existing.slotIntervalMinutes,
            bufferBeforeMinutes: existing.bufferBeforeMinutes,
            bufferAfterMinutes: existing.bufferAfterMinutes,
            minimumNoticeMinutes: existing.minimumNoticeMinutes,
            bookingHorizonDays: existing.bookingHorizonDays,
            price: existing.price ?? undefined,
            isEnabled: existing.isEnabled,
          }
        : DEFAULTS
    );
  }, [isOpen, existing, reset]);

  const err = (field: keyof AppointmentTypeInput) =>
    errors[field]?.message ?? fieldErrors?.[field] ?? fieldErrors?.[
      field.charAt(0).toUpperCase() + field.slice(1)
    ];

  return (
    <Dialog
      open={isOpen}
      onOpenChange={(open) => {
        if (!open) {
          close();
          clearFieldErrors();
        }
      }}
    >
      <DialogContent className="sm:max-w-lg max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{existing ? "Edit service" : "Add service"}</DialogTitle>
          <DialogDescription>
            Duration is what the customer is told. Buffers block the calendar around it without
            appearing in the booking.
          </DialogDescription>
        </DialogHeader>

        <form
          id="type-form"
          className="space-y-4"
          onSubmit={handleSubmit((data) => mutate({ id: selectedId, data }))}
        >
          <div className="space-y-2">
            <Label htmlFor="name">Name</Label>
            <Input id="name" {...register("name")} placeholder="Consultation" />
            {err("name") && <p className="text-sm text-destructive">{err("name")}</p>}
          </div>

          <div className="space-y-2">
            <Label htmlFor="description">Description (optional)</Label>
            <Textarea id="description" rows={2} {...register("description")} />
          </div>

          {/* Single column on phones: two columns of number inputs plus their labels wrap awkwardly
              below ~380px, and these labels ("Slots start every (min)") are long. */}
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="durationMinutes">Duration (min)</Label>
              <Input id="durationMinutes" type="number" {...register("durationMinutes")} />
              {err("durationMinutes") && (
                <p className="text-sm text-destructive">{err("durationMinutes")}</p>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="slotIntervalMinutes">Slots start every (min)</Label>
              <Input id="slotIntervalMinutes" type="number" {...register("slotIntervalMinutes")} />
              {err("slotIntervalMinutes") && (
                <p className="text-sm text-destructive">{err("slotIntervalMinutes")}</p>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="bufferBeforeMinutes">Buffer before (min)</Label>
              <Input id="bufferBeforeMinutes" type="number" {...register("bufferBeforeMinutes")} />
            </div>
            <div className="space-y-2">
              <Label htmlFor="bufferAfterMinutes">Buffer after (min)</Label>
              <Input id="bufferAfterMinutes" type="number" {...register("bufferAfterMinutes")} />
            </div>
            <div className="space-y-2">
              <Label htmlFor="minimumNoticeMinutes">Minimum notice (min)</Label>
              <Input id="minimumNoticeMinutes" type="number" {...register("minimumNoticeMinutes")} />
            </div>
            <div className="space-y-2">
              <Label htmlFor="bookingHorizonDays">Bookable up to (days)</Label>
              <Input id="bookingHorizonDays" type="number" {...register("bookingHorizonDays")} />
              {err("bookingHorizonDays") && (
                <p className="text-sm text-destructive">{err("bookingHorizonDays")}</p>
              )}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="price">Price (optional, display only)</Label>
            <Input id="price" type="number" step="0.01" {...register("price")} />
          </div>
        </form>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={close} disabled={isPending}>
            Cancel
          </Button>
          <Button type="submit" form="type-form" disabled={isPending}>
            {isPending && <Spinner className="size-4" />}
            {existing ? "Save changes" : "Add service"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/**
 * The service's configuration, as separate facts rather than one run-on line.
 *
 * Duration is deliberately not here — it defines the service, so it is promoted next to the name.
 * What is left is setup detail: worth showing, not worth competing with the name for attention.
 * Separated by whitespace rather than punctuation so each fact reads on its own.
 */
function ServiceFacts({ t }: { t: AppointmentType }) {
  const facts = [
    `Slots every ${t.slotIntervalMinutes} min`,
    `${t.minimumNoticeMinutes} min notice`,
    `Books ${t.bookingHorizonDays} days ahead`,
  ];

  if (t.bufferBeforeMinutes > 0 || t.bufferAfterMinutes > 0) {
    facts.push(`${t.bufferBeforeMinutes}/${t.bufferAfterMinutes} min buffers`);
  }
  // Configurable on the form but previously never shown anywhere once saved.
  if (t.price != null) facts.push(`Price ${t.price.toLocaleString()}`);

  return (
    <div className="mt-1 flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-muted-foreground">
      {facts.map((f) => (
        <span key={f}>{f}</span>
      ))}
    </div>
  );
}

export function AppointmentTypeSection() {
  const { data: types, isLoading } = useAppointmentTypes();
  const { open } = useTypeFormDialog();
  const remove = useDeleteAppointmentType();

  // Retiring a service is one click from a bare icon, so it gets a confirmation step. Existing
  // appointments keep working, which the prompt says plainly rather than leaving people to guess.
  const [confirming, setConfirming] = useState<AppointmentType | null>(null);

  return (
    <Card>
      <CardHeader className="flex flex-row items-start justify-between gap-4 space-y-0">
        <div>
          <CardTitle>Bookable services</CardTitle>
          <CardDescription>
            What customers can book, and how long each one takes.
          </CardDescription>
        </div>
        <Button type="button" size="sm" onClick={() => open()}>
          <Plus className="size-4" /> Add service
        </Button>
      </CardHeader>
      <CardContent>
        {isLoading ? (
          <Skeleton className="h-24 w-full" />
        ) : types && types.length > 0 ? (
          <ul className="divide-y overflow-hidden rounded-lg border">
            {types.map((t) => (
              <li
                key={t.id}
                className={cn(
                  "flex items-start justify-between gap-4 px-4 py-3",
                  !t.isEnabled && "bg-muted/30"
                )}
              >
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="truncate font-medium">{t.name}</span>
                    {/* The defining fact, given the weight to match. */}
                    <Badge variant="secondary" className="font-normal tabular-nums">
                      {t.durationMinutes} min
                    </Badge>
                    {!t.isEnabled && <Badge variant="outline">Not bookable</Badge>}
                  </div>

                  {t.description && (
                    <p className="mt-1 line-clamp-2 text-sm text-muted-foreground">
                      {t.description}
                    </p>
                  )}

                  <ServiceFacts t={t} />
                </div>

                <div className="flex shrink-0 items-center gap-1">
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="size-8 text-muted-foreground hover:text-foreground"
                    aria-label={`Edit ${t.name}`}
                    onClick={() => open(t.id)}
                  >
                    <Pencil className="size-4" />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="size-8 text-muted-foreground hover:text-destructive"
                    aria-label={`Retire ${t.name}`}
                    onClick={() => setConfirming(t)}
                  >
                    <Trash2 className="size-4" />
                  </Button>
                </div>
              </li>
            ))}
          </ul>
        ) : (
          <div className="rounded-lg border border-dashed px-4 py-8 text-center">
            <p className="text-sm text-muted-foreground">
              Nothing can be booked yet. Add a service to open the calendar.
            </p>
            <Button type="button" variant="outline" size="sm" className="mt-3" onClick={() => open()}>
              <Plus className="size-4" /> Add service
            </Button>
          </div>
        )}
      </CardContent>

      <TypeFormDialog types={types ?? []} />

      <AlertDialog open={!!confirming} onOpenChange={(o) => !o && setConfirming(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Retire {confirming?.name}?</AlertDialogTitle>
            <AlertDialogDescription>
              Customers will no longer be able to book it. Appointments already booked are not
              affected and stay on the calendar.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={remove.isPending}>Keep it</AlertDialogCancel>
            <AlertDialogAction
              disabled={remove.isPending}
              onClick={() => {
                if (confirming) remove.mutate(confirming.id, { onSuccess: () => setConfirming(null) });
              }}
            >
              {remove.isPending && <Spinner className="size-4" />}
              Retire service
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Card>
  );
}
