import Link from "next/link";
import {
  Building2,
  CheckCheck,
  Contact,
  CreditCard,
  Eye,
  Inbox,
  Layers,
  ListChecks,
  Megaphone,
  Plus,
  Send,
  Smartphone,
  Users,
} from "lucide-react";
import { auth } from "@/auth";
import { Button } from "@/components/ui/button";
import { getCompaniesAction } from "@/features/companies/actions/company-actions";
import { getPlansAction } from "@/features/plans/actions/plan-actions";
import { getSubscriptionsAction } from "@/features/subscriptions/actions/subscription-actions";
import { getCampaignPerformanceAction, getMessageMetricsAction } from "@/features/analytics/actions/analytics-actions";
import { getContactsAction } from "@/features/contacts/actions/contact-actions";
import { getConversationsAction } from "@/features/conversations/actions/conversation-actions";
import { DashboardGreeting } from "@/features/dashboard/components/dashboard-greeting";
import { KpiCard } from "@/features/dashboard/components/kpi-card";
import { ActivityChart } from "@/features/dashboard/components/activity-chart";
import { RecentConversations } from "@/features/dashboard/components/recent-conversations";
import { RecentCampaigns } from "@/features/dashboard/components/recent-campaigns";
import { QuickLinkCard } from "@/features/dashboard/components/quick-link-card";
import { CurrentPlanCard } from "@/features/my-subscription/components/current-plan-card";
import { change, pct } from "@/features/dashboard/format";

const isoDay = (d: Date) => d.toISOString().slice(0, 10);
const daysAgo = (n: number) => {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return d;
};

export default async function DashboardPage() {
  const session = await auth();
  const user = session?.user;
  const role = user?.role ?? "";

  return (
    <div className="flex flex-1 flex-col gap-4 p-4 md:gap-6 md:p-6">
      {role === "SuperAdmin" ? (
        <SuperAdminContent name={user?.name ?? ""} />
      ) : role === "CompanyAdmin" ? (
        <CompanyAdminContent name={user?.name ?? ""} />
      ) : (
        <AgentContent name={user?.name ?? ""} />
      )}
    </div>
  );
}

// ── Company admin: numbers, activity, chats and campaigns ──────────────────
async function CompanyAdminContent({ name }: { name: string }) {
  const to = isoDay(new Date());
  const [metrics, campaigns, contacts, conversations] = await Promise.all([
    getMessageMetricsAction(isoDay(daysAgo(29)), to),
    getCampaignPerformanceAction(isoDay(daysAgo(89)), to),
    getContactsAction({ page: 1, pageSize: 1 }),
    getConversationsAction({ page: 1, pageSize: 6 }),
  ]);

  const m = metrics.success ? metrics.data : null;
  const daily = m?.daily ?? [];

  // Week-over-week trend on messages sent, from the daily series.
  const sorted = [...daily].sort((a, b) => a.date.localeCompare(b.date));
  const sum = (rows: typeof sorted) => rows.reduce((t, r) => t + r.sent, 0);
  const sentTrend = sorted.length >= 14 ? change(sum(sorted.slice(-7)), sum(sorted.slice(-14, -7))) : null;

  const deliveryRate = m ? pct(m.totalDelivered, m.totalSent) : null;
  const readRate = m ? pct(m.totalRead, m.totalSent) : null;
  const contactsTotal = contacts.success ? contacts.data.pagination.total : null;

  const recentCampaigns = campaigns.success
    ? [...campaigns.data.campaigns]
        .sort((a, b) => (b.launchedAt ?? "").localeCompare(a.launchedAt ?? ""))
        .slice(0, 4)
    : null;

  return (
    <>
      <DashboardGreeting name={name} subtitle="Here's how your WhatsApp messaging is doing.">
        <Button asChild variant="outline" size="lg">
          <Link href="/inbox">
            <Inbox data-icon="inline-start" /> Inbox
          </Link>
        </Button>
        <Button asChild size="lg">
          <Link href="/campaigns">
            <Plus data-icon="inline-start" /> New campaign
          </Link>
        </Button>
      </DashboardGreeting>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard
          label="Messages sent"
          icon={Send}
          value={m ? m.totalSent.toLocaleString() : null}
          trend={sentTrend}
          footnote={sentTrend === null ? "Last 30 days" : "Last 30 days · trend is week over week"}
        />
        <KpiCard
          label="Delivery rate"
          icon={CheckCheck}
          value={deliveryRate === null ? (m ? "0%" : null) : `${deliveryRate}%`}
          footnote={m ? `${m.totalFailed.toLocaleString()} failed in the last 30 days` : "Last 30 days"}
        />
        <KpiCard
          label="Read rate"
          icon={Eye}
          value={readRate === null ? (m ? "0%" : null) : `${readRate}%`}
          footnote={m ? `${m.totalRead.toLocaleString()} messages opened` : "Last 30 days"}
        />
        <KpiCard
          label="Contacts"
          icon={Contact}
          value={contactsTotal === null ? null : contactsTotal.toLocaleString()}
          footnote="People you can message"
        />
      </div>

      <div className="grid gap-4 xl:grid-cols-3">
        <div className="xl:col-span-2">
          <ActivityChart daily={daily} />
        </div>
        <CurrentPlanCard />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <RecentConversations items={conversations.success ? conversations.data.items : null} />
        <RecentCampaigns rows={recentCampaigns} />
      </div>
    </>
  );
}

// ── Agent: no analytics access, so a greeting and shortcuts ────────────────
function AgentContent({ name }: { name: string }) {
  const links = [
    { href: "/inbox", title: "Inbox", description: "Reply to your WhatsApp conversations.", icon: Inbox },
    { href: "/contacts", title: "Contacts", description: "Look up the people you message.", icon: Contact },
    { href: "/contact-lists", title: "Contact lists", description: "Groups of contacts for campaigns.", icon: ListChecks },
  ];
  return (
    <>
      <DashboardGreeting name={name} subtitle="Jump back into your conversations and contacts.">
        <Button asChild size="lg">
          <Link href="/inbox">
            <Inbox data-icon="inline-start" /> Open inbox
          </Link>
        </Button>
      </DashboardGreeting>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {links.map((l, i) => (
          <QuickLinkCard key={l.href} {...l} index={i} />
        ))}
      </div>
    </>
  );
}

// ── Super-admin: platform totals + shortcuts ───────────────────────────────
async function SuperAdminContent({ name }: { name: string }) {
  const [companies, subscriptions, plans] = await Promise.all([
    getCompaniesAction({ page: 1, pageSize: 1 }),
    getSubscriptionsAction({ page: 1, pageSize: 1 }),
    getPlansAction({ page: 1, pageSize: 1 }),
  ]);
  const total = (r: { success: boolean; data?: { pagination: { total: number } } }) =>
    r.success && r.data ? r.data.pagination.total.toLocaleString() : null;

  const links = [
    { href: "/superadmin/companies", title: "Companies", description: "Provision and manage tenant companies.", icon: Building2 },
    { href: "/superadmin/waba-connections", title: "WABA connections", description: "Connect WhatsApp Business numbers.", icon: Smartphone },
    { href: "/superadmin/plans", title: "Plans", description: "Define subscription tiers and quotas.", icon: Layers },
    { href: "/superadmin/subscriptions", title: "Subscriptions", description: "Assign companies to plans, track usage.", icon: CreditCard },
    { href: "/superadmin/users", title: "Users", description: "Manage tenant admins and agents.", icon: Users },
    { href: "/superadmin/reports", title: "Reports", description: "Usage and cost across the platform.", icon: Megaphone },
  ];

  return (
    <>
      <DashboardGreeting name={name} subtitle="Here's what's happening across your platform." />
      <div className="grid gap-4 sm:grid-cols-3">
        <KpiCard label="Companies" icon={Building2} value={total(companies)} footnote="Tenants on the platform" />
        <KpiCard label="Subscriptions" icon={CreditCard} value={total(subscriptions)} footnote="Companies on a plan" />
        <KpiCard label="Plans" icon={Layers} value={total(plans)} footnote="Available tiers" />
      </div>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {links.map((l, i) => (
          <QuickLinkCard key={l.href} {...l} index={i} />
        ))}
      </div>
    </>
  );
}
