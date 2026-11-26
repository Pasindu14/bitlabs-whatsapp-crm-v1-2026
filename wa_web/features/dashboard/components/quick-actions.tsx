import Link from "next/link";
import { FilePlus2, ListPlus, Megaphone, MessageSquarePlus, Upload, UserPlus } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";

// `?action=` is read by useOpenFromQuery on the target page, which opens the dialog straight away.
const ACTIONS = [
  { href: "/campaigns?action=new", title: "New campaign", hint: "Message a whole list", icon: Megaphone, tone: "bg-[#dcf3e4] text-[#0f6b3e]" },
  { href: "/inbox?action=new", title: "New chat", hint: "Message one customer", icon: MessageSquarePlus, tone: "bg-[#dcebfa] text-[#1b5e9e]" },
  { href: "/templates/new", title: "New template", hint: "Get it approved by Meta", icon: FilePlus2, tone: "bg-[#ece4fb] text-[#5b3aa8]" },
  { href: "/contacts?action=new", title: "Add contact", hint: "Save one number", icon: UserPlus, tone: "bg-[#fbe7d3] text-[#9a4f0f]" },
  { href: "/contacts?action=import", title: "Import contacts", hint: "Upload a file", icon: Upload, tone: "bg-[#fbe0e6] text-[#a32648]" },
  { href: "/contact-lists?action=new", title: "New contact list", hint: "Group your audience", icon: ListPlus, tone: "bg-[#d8f0f0] text-[#126b6b]" },
];

/** One-click shortcuts to the things a company admin creates most. */
export function QuickActions() {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-[15px]">Quick actions</CardTitle>
        <CardDescription>Jump straight to what you want to do.</CardDescription>
      </CardHeader>
      <CardContent className="grid grid-cols-2 gap-3 md:grid-cols-3 2xl:grid-cols-6">
        {ACTIONS.map(({ href, title, hint, icon: Icon, tone }) => (
          <Link
            key={href}
            href={href}
            className={cn(
              "flex items-center gap-3 rounded-xl border bg-card p-3 transition-all",
              "hover:-translate-y-0.5 hover:border-primary/40 hover:shadow-md hover:shadow-primary/5",
              "focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none",
            )}
          >
            <span className={cn("flex size-10 shrink-0 items-center justify-center rounded-lg", tone)}>
              <Icon className="size-5" />
            </span>
            <span className="min-w-0 leading-tight">
              <span className="block truncate text-[13.5px] font-medium">{title}</span>
              <span className="block truncate text-[12px] text-muted-foreground">{hint}</span>
            </span>
          </Link>
        ))}
      </CardContent>
    </Card>
  );
}
