import { redirect } from "next/navigation";

// Authenticated users land on the dashboard; unauthenticated requests are
// redirected to /sign-in by proxy.ts before this ever renders.
export default function Home() {
  redirect("/dashboard");
}
