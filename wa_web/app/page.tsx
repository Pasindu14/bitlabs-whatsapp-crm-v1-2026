import { auth } from "@/auth";
import { redirect } from "next/navigation";

export default async function Home() {
  const session = await auth();
  const role = session?.user?.role?.toLowerCase();
  redirect(role === "superadmin" ? "/superadmin/companies" : "/dashboard");
}
