"use client";
import {
  Building2,
  LayoutDashboard,
  MessageCircle,
  Users,
  type LucideIcon,
} from "lucide-react";
import { useSession } from "next-auth/react";
import { NavMain } from "@/components/nav-main";
import { NavUser } from "@/components/nav-user";
import { CompanyLogo } from "@/components/company-logo";
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarHeader,
  SidebarRail,
} from "@/components/ui/sidebar";

type NavSubItem = {
  title: string;
  url: string;
  roles?: string[];
};

type NavGroup = {
  title: string;
  url: string;
  icon: LucideIcon;
  isActive?: boolean;
  roles?: string[];
  items: NavSubItem[];
};

// Roles mirror wa_api UserRole: SuperAdmin | CompanyAdmin | Agent.
const navConfig: NavGroup[] = [
  {
    title: "Overview",
    url: "#",
    icon: LayoutDashboard,
    isActive: true,
    roles: ["CompanyAdmin", "Agent"],
    items: [
      { title: "Dashboard", url: "/dashboard" },
    ],
  },
  // ── Platform administration (SuperAdmin only) ──────────────────
  {
    title: "Platform",
    url: "#",
    icon: Building2,
    isActive: true,
    roles: ["SuperAdmin"],
    items: [
      { title: "Companies", url: "/superadmin/companies" },
      { title: "WABA Connections", url: "/superadmin/waba-connections" },
      { title: "Users", url: "/superadmin/users" },
    ],
  },
  // ── Company workspace (CompanyAdmin / Agent) ───────────────────
  {
    title: "Messaging",
    url: "#",
    icon: MessageCircle,
    isActive: false,
    roles: ["CompanyAdmin", "Agent"],
    items: [
      { title: "Inbox", url: "/inbox" },
      { title: "Campaigns", url: "/campaigns" },
      { title: "Messages", url: "/messages" },
      { title: "Contacts", url: "/contacts" },
      { title: "Contact Lists", url: "/contact-lists" },
    ],
  },
  {
    title: "Company Settings",
    url: "#",
    icon: Users,
    isActive: false,
    roles: ["CompanyAdmin"],
    items: [
      { title: "Team", url: "/team" },
      { title: "Connections", url: "/connections" },
    ],
  },
];

export function AppSidebar({ ...props }: React.ComponentProps<typeof Sidebar>) {
  const { data: session } = useSession();
  const userRole = session?.user?.role ?? "";

  const filteredNav = navConfig
    .filter((group) => !group.roles || group.roles.includes(userRole))
    .map((group) => ({
      title: group.title,
      url: group.url,
      icon: group.icon,
      isActive: group.isActive,
      items: group.items.filter(
        (item) => !item.roles || item.roles.includes(userRole),
      ),
    }));

  return (
    <Sidebar collapsible="icon" {...props}>
      <SidebarHeader>
        <CompanyLogo />
      </SidebarHeader>
      <SidebarContent>
        <NavMain items={filteredNav} />
      </SidebarContent>
      <SidebarFooter>
        <NavUser />
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
  );
}
