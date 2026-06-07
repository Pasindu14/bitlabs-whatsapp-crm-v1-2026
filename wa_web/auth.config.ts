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
      const userRole = auth?.user?.role;
      const path = nextUrl.pathname;

      // Always allow the unauthorized page (avoid redirect loops)
      if (path === "/unauthorized") return true;

      // Redirect logged-in users away from auth pages
      if ((path === "/sign-in" || path === "/login") && isLoggedIn) {
        if (userRole?.toLowerCase() === "distributor") {
          return Response.redirect(new URL("/distributor-dashboard", nextUrl));
        }
        return Response.redirect(new URL("/dashboard", nextUrl));
      }

      // Public auth pages: allow unauthenticated access
      if (path === "/sign-in" || path === "/login") return true;

      // All other routes are protected — require login
      if (!isLoggedIn) {
        return Response.redirect(new URL("/sign-in", nextUrl));
      }

      // Distributor portal — only Distributor role may enter
      if (path.startsWith("/distributor-dashboard") || path.startsWith("/distributor-stock") || path.startsWith("/distributor-billings") || path.startsWith("/distributor-grns") || path.startsWith("/distributor-purchase-orders") || path.startsWith("/distributor-stock-taking") || path.startsWith("/portal/")) {
        if (userRole?.toLowerCase() !== "distributor") {
          return Response.redirect(new URL("/unauthorized", nextUrl));
        }
        return true;
      }

      // All remaining routes require Admin
      if (userRole?.toLowerCase() !== "admin") {
        return Response.redirect(new URL("/unauthorized", nextUrl));
      }

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
