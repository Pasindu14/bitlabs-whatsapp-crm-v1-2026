import { Mail, Shield, Building2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";

interface AccountCardProps {
  email: string;
  role: string;
  companyId: string | null;
}

const ROLE_LABEL: Record<string, string> = {
  SuperAdmin: "Platform Owner",
  CompanyAdmin: "Company Admin",
  Agent: "Agent",
};

/** Compact identity panel for the tenant dashboard. */
export function AccountCard({ email, role, companyId }: AccountCardProps) {
  return (
    <div className="rounded-xl border bg-card p-5 shadow-sm">
      <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Account</p>

      <dl className="mt-4 space-y-3.5 text-sm">
        <Row icon={Mail} label="Email">
          <span className="truncate font-medium">{email || "—"}</span>
        </Row>
        <Row icon={Shield} label="Role">
          <Badge variant="secondary" className="font-medium">
            {ROLE_LABEL[role] ?? role}
          </Badge>
        </Row>
        <Row icon={Building2} label="Workspace">
          <span className="truncate font-mono text-xs text-muted-foreground">
            {companyId ?? "Platform (no company)"}
          </span>
        </Row>
      </dl>
    </div>
  );
}

function Row({
  icon: Icon,
  label,
  children,
}: {
  icon: typeof Mail;
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div className="flex items-center gap-3">
      <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-muted text-muted-foreground">
        <Icon className="h-4 w-4" />
      </div>
      <div className="flex min-w-0 flex-1 items-center justify-between gap-3">
        <span className="text-muted-foreground">{label}</span>
        <div className="min-w-0 text-right">{children}</div>
      </div>
    </div>
  );
}
