import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { TemplateBuilder } from "@/features/templates/components/template-builder";

export default async function NewTemplatePage() {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }
  return <TemplateBuilder />;
}
