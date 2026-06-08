import Link from "next/link";
import { ArrowUpRight, type LucideIcon } from "lucide-react";
import { cn } from "@/lib/utils";

interface QuickLinkCardProps {
  href: string;
  title: string;
  description: string;
  icon: LucideIcon;
  /** Stagger index for the entrance animation. */
  index?: number;
}

/**
 * A navigational tile for a dashboard section. Presentational only (no hooks) so it can
 * render inside the server-component dashboard. Hover lifts the card, tints the border with
 * the brand green, and slides the corner arrow.
 */
export function QuickLinkCard({ href, title, description, icon: Icon, index = 0 }: QuickLinkCardProps) {
  return (
    <Link
      href={href}
      className={cn(
        "group relative flex flex-col gap-3 overflow-hidden rounded-xl border bg-card p-5",
        "transition-all duration-300 hover:-translate-y-0.5 hover:border-primary/40 hover:shadow-md hover:shadow-primary/5",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
        "animate-in fade-in slide-in-from-bottom-2 duration-500",
      )}
      style={{ animationDelay: `${120 + index * 70}ms`, animationFillMode: "backwards" }}
    >
      {/* Hover wash */}
      <div
        aria-hidden
        className="pointer-events-none absolute inset-0 bg-gradient-to-br from-primary/5 to-transparent opacity-0 transition-opacity duration-300 group-hover:opacity-100"
      />

      <div className="relative flex items-start justify-between">
        <div className="flex h-11 w-11 items-center justify-center rounded-lg border border-primary/15 bg-primary/10 text-primary transition-colors duration-300 group-hover:bg-primary group-hover:text-primary-foreground">
          <Icon className="h-5 w-5" />
        </div>
        <ArrowUpRight className="h-4 w-4 text-muted-foreground transition-all duration-300 group-hover:-translate-y-0.5 group-hover:translate-x-0.5 group-hover:text-primary" />
      </div>

      <div className="relative space-y-1">
        <h3 className="font-semibold leading-none tracking-tight">{title}</h3>
        <p className="text-sm text-muted-foreground">{description}</p>
      </div>
    </Link>
  );
}
