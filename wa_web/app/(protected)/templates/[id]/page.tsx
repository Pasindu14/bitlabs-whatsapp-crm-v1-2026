import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { TemplateBuilder } from "@/features/templates/components/template-builder";

// Next 16: route params are async and must be awaited (see node_modules/next/dist/docs).
export default async function TemplateDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }
  const { id } = await params;
  return <TemplateBuilder templateId={id} />;
}
