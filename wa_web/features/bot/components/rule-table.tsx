"use client";

import { Bot, Pencil, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Switch } from "@/components/ui/switch";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  useAutoReplyRules,
  useToggleAutoReplyRule,
} from "@/features/bot/hooks/use-auto-reply-rules";
import {
  useCreateRuleDialog,
  useEditRuleDialog,
  useDeleteRuleDialog,
} from "@/features/bot/store/bot-store";
import { TERMLESS_TRIGGERS, TRIGGER_LABELS, type AutoReplyRule } from "@/features/bot/types";
import { RuleDialogs } from "./rule-dialogs";

function TermsCell({ rule }: { rule: AutoReplyRule }) {
  if (TERMLESS_TRIGGERS.includes(rule.trigger)) {
    return <span className="text-muted-foreground">—</span>;
  }

  return (
    <div className="flex flex-wrap gap-1">
      {rule.terms.map((term) => (
        <Badge key={term} variant="secondary" className="font-normal">
          {term}
        </Badge>
      ))}
    </div>
  );
}

export function RuleTable() {
  const { data: rules, isLoading, isError } = useAutoReplyRules();
  const { open: openCreate } = useCreateRuleDialog();
  const { open: openEdit } = useEditRuleDialog();
  const { open: openDelete } = useDeleteRuleDialog();
  const toggle = useToggleAutoReplyRule();

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-end">
        <Button onClick={openCreate}>
          <Plus className="mr-2 size-4" />
          Add Rule
        </Button>
      </div>

      <div className="rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-20">Priority</TableHead>
              <TableHead>Name</TableHead>
              <TableHead>Trigger</TableHead>
              <TableHead>Terms</TableHead>
              <TableHead>Reply</TableHead>
              <TableHead className="w-24">Enabled</TableHead>
              <TableHead className="w-24 text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading &&
              Array.from({ length: 3 }).map((_, i) => (
                <TableRow key={i}>
                  <TableCell colSpan={7}>
                    <Skeleton className="h-6 w-full" />
                  </TableCell>
                </TableRow>
              ))}

            {isError && (
              <TableRow>
                <TableCell colSpan={7} className="py-10 text-center text-muted-foreground">
                  Couldn&apos;t load rules. Refresh to try again.
                </TableCell>
              </TableRow>
            )}

            {!isLoading && !isError && rules?.length === 0 && (
              <TableRow>
                <TableCell colSpan={7} className="py-12 text-center">
                  <Bot className="mx-auto mb-3 size-8 text-muted-foreground" />
                  <p className="font-medium">No rules yet</p>
                  <p className="text-sm text-muted-foreground">
                    Add a rule and the bot starts replying to incoming WhatsApp messages.
                  </p>
                </TableCell>
              </TableRow>
            )}

            {rules?.map((rule) => (
              <TableRow key={rule.id}>
                <TableCell className="tabular-nums text-muted-foreground">
                  {rule.priority}
                </TableCell>
                <TableCell className="font-medium">{rule.name}</TableCell>
                <TableCell>
                  <Badge variant="outline">{TRIGGER_LABELS[rule.trigger]}</Badge>
                </TableCell>
                <TableCell className="max-w-56">
                  <TermsCell rule={rule} />
                </TableCell>
                <TableCell className="max-w-72 truncate text-muted-foreground">
                  {rule.replyBody}
                </TableCell>
                <TableCell>
                  <Switch
                    checked={rule.isEnabled}
                    disabled={toggle.isPending}
                    aria-label={rule.isEnabled ? "Disable rule" : "Enable rule"}
                    // Toggling is a full update — the API has no dedicated endpoint, so the
                    // rest of the rule is sent back unchanged.
                    onCheckedChange={(checked) =>
                      toggle.mutate({
                        id: rule.id,
                        data: {
                          name: rule.name,
                          trigger: rule.trigger,
                          terms: rule.terms,
                          replyBody: rule.replyBody,
                          priority: rule.priority,
                          isEnabled: checked,
                        },
                      })
                    }
                  />
                </TableCell>
                <TableCell className="text-right">
                  <Button
                    variant="ghost"
                    size="icon"
                    aria-label="Edit rule"
                    onClick={() => openEdit(rule.id)}
                  >
                    <Pencil className="size-4" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    aria-label="Delete rule"
                    onClick={() => openDelete(rule.id)}
                  >
                    <Trash2 className="size-4 text-destructive" />
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      <RuleDialogs />
    </div>
  );
}
