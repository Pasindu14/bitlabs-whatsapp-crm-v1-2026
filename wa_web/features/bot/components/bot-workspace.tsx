"use client";

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
    <Tabs defaultValue="assistant" className="gap-6">
      <TabsList>
        <TabsTrigger value="assistant">AI assistant</TabsTrigger>
        <TabsTrigger value="prompt">AI prompt</TabsTrigger>
        <TabsTrigger value="rules">Keyword rules</TabsTrigger>
        {/* Named for what it is to every business; the clinic CRM is the one integration so far. */}
        <TabsTrigger value="integrations">Integrations</TabsTrigger>
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
