"use client";
import {
  Activity,
  BarChart3,
  Bot,
  Building2,
  CalendarDays,
  Contact,
  CreditCard,
  FileClock,
  FileText,
  Inbox,
  Layers,
  LayoutDashboard,
  ListChecks,
  Megaphone,
  Package,
  PieChart,
  Plug,
  Smartphone,
  UserCog,
  Users,
  type LucideIcon,
} from "lucide-react";
import { useSession } from "next-auth/react";
import { NavMain, type NavSection } from "@/components/nav-main";
import { NavUser } from "@/components/nav-user";
import { CompanyLogo } from "@/components/company-logo";
import { useBotAccess } from "@/features/bot/hooks/use-bot-access";
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarHeader,
  SidebarRail,
} from "@/components/ui/sidebar";

type NavItem = {
  title: string;
  url: string;
  icon: LucideIcon;
  roles?: string[];
  /** Only shown when the company's plan includes this feature (plan 005). */
  feature?: "chatbot" | "calendar";
};

type NavGroup = { label: string; roles: string[]; items: NavItem[] };

// Roles mirror wa_api UserRole: SuperAdmin | CompanyAdmin | Agent.
const navConfig: NavGroup[] = [
  // ── Platform administration (SuperAdmin only) ──────────────────
  {
    label: "Platform",
    roles: ["SuperAdmin"],
    items: [
      { title: "Companies", url: "/superadmin/companies", icon: Building2 },
      { title: "WhatsApp numbers", url: "/superadmin/waba-connections", icon: Smartphone },
      { title: "Plans", url: "/superadmin/plans", icon: Layers },
      { title: "Packages", url: "/superadmin/packages", icon: Package },
      { title: "Subscriptions", url: "/superadmin/subscriptions", icon: CreditCard },
      { title: "Users", url: "/superadmin/users", icon: UserCog },
    ],
  },
  {
    label: "Health",
    roles: ["SuperAdmin"],
    items: [
      { title: "Monitoring", url: "/superadmin/monitoring", icon: Activity },
      { title: "Reports", url: "/superadmin/reports", icon: PieChart },
      { title: "Logs", url: "/superadmin/server-logs", icon: FileClock },
    ],
  },
  // ── Company workspace (CompanyAdmin / Agent) ───────────────────
  {
    label: "Workspace",
    roles: ["CompanyAdmin", "Agent"],
    items: [
      { title: "Dashboard", url: "/dashboard", icon: LayoutDashboard },
      { title: "Inbox", url: "/inbox", icon: Inbox },
      { title: "Campaigns", url: "/campaigns", icon: Megaphone },
      { title: "Chatbot", url: "/bot", icon: Bot, roles: ["CompanyAdmin"], feature: "chatbot" },
      { title: "Calendar", url: "/calendar", icon: CalendarDays, roles: ["CompanyAdmin"], feature: "calendar" },
    ],
  },
  {
    label: "Audience and content",
    roles: ["CompanyAdmin", "Agent"],
    items: [
      { title: "Contacts", url: "/contacts", icon: Contact },
      { title: "Contact lists", url: "/contact-lists", icon: ListChecks },
      { title: "Templates", url: "/templates", icon: FileText },
    ],
  },
  {
    label: "Company",
    roles: ["CompanyAdmin"],
    items: [
      { title: "Reports", url: "/analytics", icon: BarChart3 },
      { title: "Team", url: "/team", icon: Users },
      { title: "Connections", url: "/connections", icon: Plug },
      { title: "Subscription", url: "/my-subscription", icon: CreditCard },
    ],
  },
];

export function AppSidebar({ ...props }: React.ComponentProps<typeof Sidebar>) {
  const { data: session } = useSession();
  const userRole = session?.user?.role ?? "";
  const botAccess = useBotAccess();

  const sections: NavSection[] = navConfig
    .filter((group) => group.roles.includes(userRole))
    .map((group) => ({
      label: group.label,
      items: group.items.filter(
        (item) =>
          (!item.roles || item.roles.includes(userRole)) &&
          (!item.feature || botAccess[item.feature]),
      ),
    }))
    .filter((group) => group.items.length > 0);

  return (
    <Sidebar collapsible="icon" {...props}>
      <SidebarHeader>
        <CompanyLogo />
      </SidebarHeader>
      <SidebarContent>
        <NavMain sections={sections} />
      </SidebarContent>
      <SidebarFooter>
        <NavUser />
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
  );
}
