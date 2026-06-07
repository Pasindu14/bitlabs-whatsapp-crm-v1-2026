"use client";
import {
  Cog,
  LayoutDashboard,
  Map,
  Package,
  ShoppingCart,
  Store,
  UserCheck,
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

const navConfig: NavGroup[] = [
  {
    title: "Masters",
    url: "#",
    icon: Cog,
    isActive: true,
    roles: ["Admin"],
    items: [
      { title: "Users", url: "/users" },
      { title: "Distributors", url: "/distributors" },
      { title: "Products", url: "/products" },
      { title: "Product Categories", url: "/product-categories" },
      { title: "Fleets", url: "/fleets" },
      { title: "Outlets", url: "/outlets" },
      { title: "Category Pricing", url: "/product-category-pricings" },
    ],
  },
  {
    title: "Assignments",
    url: "#",
    icon: UserCheck,
    isActive: false,
    roles: ["Admin"],
    items: [
      { title: "User Assignments", url: "/user-reporting-lines" },
      { title: "Geo Assignments", url: "/geo-assignments" },
      { title: "Route Cancellations", url: "/route-cancellations" },
    ],
  },
  {
    title: "Sales",
    url: "#",
    icon: ShoppingCart,
    isActive: false,
    roles: ["Admin"],
    items: [
      { title: "Purchase Orders", url: "/purchase-orders" },
      { title: "Sales Invoices", url: "/sales-invoices" },
      { title: "Sales Targets", url: "/sales-targets" },
      { title: "GRNs", url: "/grns" },
      { title: "Stock", url: "/stock" },
      { title: "Stock Taking", url: "/stock-taking" },
    ],
  },
  {
    title: "Geography",
    url: "#",
    icon: Map,
    isActive: false,
    roles: ["Admin"],
    items: [
      { title: "Regions", url: "/regions" },
      { title: "Areas", url: "/areas" },
      { title: "Territories", url: "/territories" },
      { title: "Divisions", url: "/divisions" },
      { title: "Routes", url: "/routes" },
    ],
  },
  // ── Distributor portal ──────────────────────────
  {
    title: "Overview",
    url: "#",
    icon: LayoutDashboard,
    isActive: true,
    roles: ["Distributor"],
    items: [
      { title: "Dashboard", url: "/distributor-dashboard" },
    ],
  },
  {
    title: "Inventory",
    url: "#",
    icon: Package,
    isActive: false,
    roles: ["Distributor"],
    items: [
      { title: "Stock Balance", url: "/distributor-stock" },
      { title: "Stock Taking", url: "/distributor-stock-taking" },
      { title: "Bills", url: "/distributor-billings" },
      { title: "GRNs", url: "/distributor-grns" },
      { title: "Purchase Orders", url: "/distributor-purchase-orders" },
    ],
  },
  {
    title: "Network",
    url: "#",
    icon: Store,
    isActive: false,
    roles: ["Distributor"],
    items: [
      { title: "My Outlets", url: "/portal/outlets" },
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
