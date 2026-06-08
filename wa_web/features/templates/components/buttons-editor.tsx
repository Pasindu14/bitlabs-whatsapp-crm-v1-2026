"use client";

import { useFieldArray, Controller, type UseFormReturn } from "react-hook-form";
import { Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { BUTTON_TYPES, BUTTON_LABELS, type ButtonType } from "@/features/templates/types";
import type { BuilderFormValues } from "@/features/templates/schema/template-schema";

const emptyButton = (type: ButtonType) => ({
  type,
  text: "",
  url: "",
  urlExample: "",
  phoneNumber: "",
  example: "",
});

export function ButtonsEditor({
  form,
  disabled,
}: {
  form: UseFormReturn<BuilderFormValues>;
  disabled?: boolean;
}) {
  const {
    control,
    register,
    watch,
    formState: { errors },
  } = form;
  const { fields, append, remove } = useFieldArray({ control, name: "buttons" });

  return (
    <div className="space-y-3">
      {fields.map((field, i) => {
        const type = watch(`buttons.${i}.type`);
        const btnErr = errors.buttons?.[i];
        const url = watch(`buttons.${i}.url`) ?? "";
        return (
          <div key={field.id} className="space-y-2 rounded-lg border p-3">
            <div className="flex items-center justify-between gap-2">
              <Controller
                control={control}
                name={`buttons.${i}.type`}
                render={({ field: f }) => (
                  <Select value={f.value} onValueChange={f.onChange} disabled={disabled}>
                    <SelectTrigger className="h-8 w-52">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {BUTTON_TYPES.map((t) => (
                        <SelectItem key={t} value={t}>
                          {BUTTON_LABELS[t]}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="h-8 w-8"
                disabled={disabled}
                onClick={() => remove(i)}
                aria-label="Remove button"
              >
                <Trash2 className="h-4 w-4" />
              </Button>
            </div>

            {type !== "copy_code" && (
              <div>
                <Input placeholder="Button text" disabled={disabled} {...register(`buttons.${i}.text`)} />
                {btnErr?.text && <p className="mt-1 text-xs text-destructive">{btnErr.text.message}</p>}
              </div>
            )}

            {type === "url" && (
              <div className="space-y-2">
                <Input placeholder="https://example.com/{{1}}" disabled={disabled} {...register(`buttons.${i}.url`)} />
                {btnErr?.url && <p className="text-xs text-destructive">{btnErr.url.message}</p>}
                {/\{\{\d+\}\}/.test(url) && (
                  <>
                    <Input
                      placeholder="Example value for {{1}} (e.g. ORD-9)"
                      disabled={disabled}
                      {...register(`buttons.${i}.urlExample`)}
                    />
                    {btnErr?.urlExample && <p className="text-xs text-destructive">{btnErr.urlExample.message}</p>}
                  </>
                )}
              </div>
            )}

            {type === "phone_number" && (
              <div>
                <Input placeholder="+15555550100" disabled={disabled} {...register(`buttons.${i}.phoneNumber`)} />
                {btnErr?.phoneNumber && <p className="mt-1 text-xs text-destructive">{btnErr.phoneNumber.message}</p>}
              </div>
            )}

            {type === "copy_code" && (
              <div>
                <Input placeholder="Sample code (e.g. SAVE20)" disabled={disabled} {...register(`buttons.${i}.example`)} />
                {btnErr?.example && <p className="mt-1 text-xs text-destructive">{btnErr.example.message}</p>}
              </div>
            )}
          </div>
        );
      })}

      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button type="button" variant="outline" size="sm" disabled={disabled || fields.length >= 10}>
            <Plus className="mr-1 h-4 w-4" />
            Add button
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="start">
          {BUTTON_TYPES.map((t) => (
            <DropdownMenuItem key={t} onClick={() => append(emptyButton(t))}>
              {BUTTON_LABELS[t]}
            </DropdownMenuItem>
          ))}
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  );
}
