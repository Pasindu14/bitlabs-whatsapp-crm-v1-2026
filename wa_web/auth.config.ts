import type { NextAuthConfig } from "next-auth";

export default {
  providers: [],
  pages: {
    signIn: "/sign-in",
    error: "/auth/error",
  },
  callbacks: {
    authorized({ auth, request: { nextUrl } }) {
      const isLoggedIn = !!auth?.user;
      const userRole = auth?.user?.role?.toLowerCase();
      const path = nextUrl.pathname;

      // Always allow the unauthorized page (avoid redirect loops).
      if (path === "/unauthorized") return true;

      const isAuthPage = path === "/sign-in" || path === "/login";

      // Logged-in users should never see the sign-in page.
      if (isAuthPage) {
        if (!isLoggedIn) return true;
        const dest = userRole === "superadmin" ? "/superadmin/companies" : "/dashboard";
        return Response.redirect(new URL(dest, nextUrl));
      }

      // Every other route requires authentication.
      if (!isLoggedIn) {
        return Response.redirect(new URL("/sign-in", nextUrl));
      }

      // SuperAdmin-only area. CompanyAdmin/Agent get bounced to /unauthorized.
      if (path.startsWith("/superadmin")) {
        if (userRole !== "superadmin") {
          return Response.redirect(new URL("/unauthorized", nextUrl));
        }
        return true;
      }

      // Dashboard is not for SuperAdmin — send them to their home.
      if (path === "/dashboard" && userRole === "superadmin") {
        return Response.redirect(new URL("/superadmin/companies", nextUrl));
      }

      // All other authenticated routes are allowed (finer role rules added per-feature).
      return true;
    },
    async jwt({ token, user }) {
      // On sign in, add user data to token
      if (user) {
        token.id = user.id;
        token.role = user.role;
        token.name = user.name;
        token.email = user.email;
        token.accessToken = user.accessToken;
      }
      return token;
    },
    async session({ session, token }) {
      // Ensure token data exists before assigning
      if (token) {
        session.user.id = token.id as string;
        session.user.role = (token.role as string);
        session.user.name = (token.name as string);
        session.user.email = token.email as string;
        session.user.accessToken = token.accessToken as string;
      }
      return session;
    },
  },
} satisfies NextAuthConfig;
