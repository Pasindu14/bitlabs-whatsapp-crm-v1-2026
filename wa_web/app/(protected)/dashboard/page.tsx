import {
  Building2,
  Smartphone,
  Layers,
  CreditCard,
  Users,
  Send,
  Contact,
  ListChecks,
} from "lucide-react";
import { auth } from "@/auth";
import { getCompaniesAction } from "@/features/companies/actions/company-actions";
import { getPlansAction } from "@/features/plans/actions/plan-actions";
import { getSubscriptionsAction } from "@/features/subscriptions/actions/subscription-actions";
import { DashboardHero } from "@/features/dashboard/components/dashboard-hero";
import { StatCard } from "@/features/dashboard/components/stat-card";
import { QuickLinkCard } from "@/features/dashboard/components/quick-link-card";
import { AccountCard } from "@/features/dashboard/components/account-card";
import { CurrentPlanCard } from "@/features/my-subscription/components/current-plan-card";

const SUBTITLE: Record<string, string> = {
  SuperAdmin: "Here's what's happening across your platform today.",
  CompanyAdmin: "Manage your messaging, contacts, and team — all from one place.",
  Agent: "Jump back into your conversations and contacts.",
};

export default async function DashboardPage() {
  const session = await auth();
  const user = session?.user;
  const role = user?.role ?? "";

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      <DashboardHero
        name={user?.name ?? ""}
        email={user?.email ?? ""}
        role={role}
        subtitle={SUBTITLE[role] ?? "Welcome to your workspace."}
      />

      {role === "SuperAdmin" ? (
        <SuperAdminContent />
      ) : (
        <CompanyContent role={role} email={user?.email ?? ""} companyId={user?.companyId ?? null} />
      )}
    </div>
  );
}

// ── Super-admin: live platform metrics + platform navigation ───────────────
async function SuperAdminContent() {
  const [companies, subscriptions, plans] = await Promise.all([
    getCompaniesAction({ page: 1, pageSize: 1 }),
    getSubscriptionsAction({ page: 1, pageSize: 1 }),
    getPlansAction({ page: 1, pageSize: 1 }),
  ]);

  const companiesTotal = companies.success ? companies.data.pagination.total : null;
  const subscriptionsTotal = subscriptions.success ? subscriptions.data.pagination.total : null;
  const plansTotal = plans.success ? plans.data.pagination.total : null;

  const links = [
    { href: "/superadmin/companies", title: "Companies", description: "Provision and manage tenant companies.", icon: Building2 },
    { href: "/superadmin/waba-connections", title: "WABA Connections", description: "Connect WhatsApp Business numbers.", icon: Smartphone },
    { href: "/superadmin/plans", title: "Plans", description: "Define subscription tiers and quotas.", icon: Layers },
    { href: "/superadmin/subscriptions", title: "Subscriptions", description: "Assign companies to plans, track usage.", icon: CreditCard },
    { href: "/superadmin/users", title: "Users", description: "Manage tenant admins and agents.", icon: Users },
  ];

  return (
    <>
      <div className="grid gap-4 sm:grid-cols-3">
        <StatCard label="Companies" value={companiesTotal} hint="Tenants on the platform" icon={Building2} index={0} />
        <StatCard label="Active Subscriptions" value={subscriptionsTotal} hint="Companies on a plan" icon={CreditCard} index={1} />
        <StatCard label="Plans" value={plansTotal} hint="Available tiers" icon={Layers} index={2} />
      </div>

      <Section title="Quick access" subtitle="Jump straight to platform administration.">
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {links.map((l, i) => (
            <QuickLinkCard key={l.href} {...l} index={i} />
          ))}
        </div>
      </Section>
    </>
  );
}

// ── Company user: plan/usage + identity + workspace navigation ─────────────
function CompanyContent({
  role,
  email,
  companyId,
}: {
  role: string;
  email: string;
  companyId: string | null;
}) {
  const links = [
    { href: "/inbox", title: "Conversations", description: "Send and review WhatsApp messages.", icon: Send },
    { href: "/contacts", title: "Contacts", description: "Manage the people you message.", icon: Contact },
    { href: "/contact-lists", title: "Contact Lists", description: "Group contacts for campaigns.", icon: ListChecks },
    ...(role === "CompanyAdmin"
      ? [{ href: "/team", title: "Team", description: "Invite users and set permissions.", icon: Users }]
      : []),
  ];

  return (
    <>
      <div className="grid gap-4 lg:grid-cols-3">
        <div className="lg:col-span-2">
          <CurrentPlanCard />
        </div>
        <AccountCard email={email} role={role} companyId={companyId} />
      </div>

      <Section title="Quick access" subtitle="Pick up where you left off.">
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {links.map((l, i) => (
            <QuickLinkCard key={l.href} {...l} index={i} />
          ))}
        </div>
      </Section>
    </>
  );
}

function Section({
  title,
  subtitle,
  children,
}: {
  title: string;
  subtitle: string;
  children: React.ReactNode;
}) {
  return (
    <section className="space-y-3">
      <div>
        <h2 className="text-lg font-semibold tracking-tight">{title}</h2>
        <p className="text-sm text-muted-foreground">{subtitle}</p>
      </div>
      {children}
    </section>
  );
}
