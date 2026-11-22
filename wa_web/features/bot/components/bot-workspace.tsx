"use client";

import { FileCode2, KeyRound, Plug, Sparkles, type LucideIcon } from "lucide-react";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { RuleTable } from "./rule-table";
import { AiSettingsView } from "./ai-settings-view";
import { PromptView } from "./prompt-view";
import { ClinicCrmSettingsView } from "@/features/clinic-crm/components/clinic-crm-settings";

/**
 * The two halves of the chatbot, in the order they run.
 *
 * Rules first is not just presentation — it is the engine's actual precedence, and putting the
 * assistant second is the clearest way to say so. A rule you write is answered word for word; the
 * assistant only sees what no rule claimed.
 */
export function BotWorkspace() {
  return (
    <Tabs defaultValue="assistant" className="gap-5">
      <TabsList variant="line" className="h-auto w-full justify-start gap-1 border-b pb-0">
        <BotTab value="assistant" icon={Sparkles} label="AI assistant" />
        <BotTab value="rules" icon={KeyRound} label="Keyword rules" />
        {/* Named for what it is to every business; the clinic CRM is the one integration so far. */}
        <BotTab value="integrations" icon={Plug} label="Integrations" />
        <BotTab value="prompt" icon={FileCode2} label="What the AI sees" />
      </TabsList>

      <TabsContent value="assistant">
        <AiSettingsView />
      </TabsContent>

      <TabsContent value="prompt">
        <PromptView />
      </TabsContent>

      <TabsContent value="rules">
        <RuleTable />
      </TabsContent>

      <TabsContent value="integrations">
        <ClinicCrmSettingsView />
      </TabsContent>
    </Tabs>
  );
}

function BotTab({ value, icon: Icon, label }: { value: string; icon: LucideIcon; label: string }) {
  return (
    <TabsTrigger
      value={value}
      className="-mb-px h-10 flex-none rounded-none border-0 border-b-2 border-transparent px-3 text-[13px] font-medium text-muted-foreground data-active:border-primary data-active:text-foreground data-[state=active]:border-primary data-[state=active]:text-foreground"
    >
      <Icon data-icon="inline-start" className="size-4" />
      {label}
    </TabsTrigger>
  );
}
