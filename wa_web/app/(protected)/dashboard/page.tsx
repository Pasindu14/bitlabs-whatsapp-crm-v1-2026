import { auth } from "@/auth";

export default async function DashboardPage() {
  const session = await auth();
  const user = session?.user;

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      <div className="space-y-1">
        <h1 className="text-2xl font-bold tracking-tight">Dashboard</h1>
        <p className="text-sm text-muted-foreground">
          Welcome back{user?.name ? `, ${user.name}` : ""}.
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <InfoCard label="Signed in as" value={user?.email ?? "—"} />
        <InfoCard label="Role" value={user?.role ?? "—"} />
        <InfoCard
          label="Company"
          value={user?.companyId ?? "Platform (no company)"}
        />
      </div>
    </div>
  );
}

function InfoCard({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border bg-card p-5 shadow-sm">
      <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </p>
      <p className="mt-1 truncate text-lg font-semibold">{value}</p>
    </div>
  );
}
