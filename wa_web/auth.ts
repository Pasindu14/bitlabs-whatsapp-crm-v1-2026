import NextAuth from "next-auth";
import authConfig from "./auth.config";
import Credentials from "next-auth/providers/credentials";
import axios from "axios";
import { env } from "@/lib/env";
import https from "https";

// Create axios instance that ignores SSL errors for local development
const apiClient = axios.create({
  httpsAgent: new https.Agent({
    rejectUnauthorized: false,
  }),
});

// How many seconds before expiry to proactively refresh the access token
const REFRESH_BUFFER_SECONDS = 60;

declare module "next-auth" {
  interface User {
    id: string;
    role: string;
    name: string | null;
    email?: string | null;
    companyId?: string | null; // null for SuperAdmin; set for tenant users
    permissions?: string[]; // capability grants (Agents only); empty for SuperAdmin/CompanyAdmin
    accessToken?: string; // JWT token from wa_api
    accessTokenExpiry?: number; // Unix timestamp (ms)
    refreshToken?: string; // opaque refresh token from wa_api
    refreshTokenExpiry?: number; // Unix timestamp (ms)
  }
  interface Session {
    user: {
      id: string;
      name: string | null;
      email?: string | null;
      role: string;
      companyId?: string | null;
      permissions?: string[];
      accessToken?: string; // JWT token from wa_api
    };
    accessToken?: string; // JWT token from wa_api
    accessTokenExpiry?: number; // Unix timestamp (ms)
    error?: "RefreshAccessTokenError";
  }
  interface JWT {
    accessToken?: string; // JWT token from wa_api
    refreshToken?: string; // opaque refresh token from wa_api
  }
}

export const { handlers, signIn, signOut, auth } = NextAuth({
  ...authConfig,
  session: {
    strategy: "jwt",
    maxAge: 30 * 24 * 60 * 60, // 30 days — matches refresh token lifetime
  },
  providers: [
    Credentials({
      name: "credentials",
      credentials: {
        email: { label: "Email", type: "email" },
        password: { label: "Password", type: "password" },
      },
      async authorize(credentials) {
        try {
          if (!credentials?.email || !credentials?.password) return null;

          const email = (credentials.email as string).trim();
          const password = credentials.password as string;
          if (email.length < 1 || password.length < 1) return null;

          // POST /api/v1/auth/login → { success, data: { accessToken, expiresAt, refreshToken, refreshTokenExpiresAt, user } }
          const response = await apiClient.post(
            `${env.API_URL}/api/v1/auth/login`,
            { email, password },
            { headers: { "Content-Type": "application/json" } },
          );

          if (!response.data?.success || !response.data?.data) {
            console.error("Login failed: unexpected response shape");
            return null;
          }

          const data = response.data.data;
          const user = data.user;

          if (process.env.NODE_ENV === "development") {
            console.log("Login successful for user:", user.id, user.role);
          }

          return {
            id: String(user.id),
            email: user.email,
            name: user.fullName,
            role: user.role,
            companyId: user.companyId ?? null,
            permissions: user.permissions ?? [],
            accessToken: data.accessToken,
            accessTokenExpiry: data.expiresAt
              ? new Date(data.expiresAt).getTime()
              : Date.now() + 8 * 60 * 60 * 1000,
            refreshToken: data.refreshToken,
            refreshTokenExpiry: data.refreshTokenExpiresAt
              ? new Date(data.refreshTokenExpiresAt).getTime()
              : Date.now() + 30 * 24 * 60 * 60 * 1000,
          };
        } catch (error) {
          console.error("Auth error:", error);
          if (axios.isAxiosError(error) && error.response?.data) {
            console.error("API Error Response:", JSON.stringify(error.response.data, null, 2));
          }
          return null;
        }
      },
    }),
  ],
  callbacks: {
    ...authConfig.callbacks,
    async jwt({ token, user }) {
      // On sign in: copy everything from the user object into the JWT.
      if (user) {
        token.id = user.id;
        token.role = user.role;
        token.name = user.name;
        token.email = user.email;
        token.companyId = user.companyId;
        token.permissions = user.permissions;
        token.accessToken = user.accessToken;
        token.accessTokenExpiry = user.accessTokenExpiry;
        token.refreshToken = user.refreshToken;
        token.refreshTokenExpiry = user.refreshTokenExpiry;
        token.error = undefined;
        return token;
      }

      // Access token still valid — nothing to do.
      const expiresAt = token.accessTokenExpiry as number | undefined;
      const isExpired = !expiresAt || Date.now() >= expiresAt - REFRESH_BUFFER_SECONDS * 1000;
      if (!isExpired) return token;

      // Access token is expired (or close to it) — try to refresh.
      const storedRefreshToken = token.refreshToken as string | undefined;
      if (!storedRefreshToken) {
        token.error = "RefreshAccessTokenError";
        return token;
      }

      try {
        // POST /api/v1/auth/refresh → new access token + rotated refresh token
        const response = await apiClient.post(
          `${env.API_URL}/api/v1/auth/refresh`,
          { refreshToken: storedRefreshToken },
          { headers: { "Content-Type": "application/json" } },
        );

        if (!response.data?.success || !response.data?.data) {
          throw new Error("Unexpected response shape from /auth/refresh");
        }

        const data = response.data.data;

        token.accessToken = data.accessToken;
        token.accessTokenExpiry = data.expiresAt
          ? new Date(data.expiresAt).getTime()
          : Date.now() + 8 * 60 * 60 * 1000;
        token.refreshToken = data.refreshToken;
        token.refreshTokenExpiry = data.refreshTokenExpiresAt
          ? new Date(data.refreshTokenExpiresAt).getTime()
          : Date.now() + 30 * 24 * 60 * 60 * 1000;
        token.error = undefined;

        if (process.env.NODE_ENV === "development") {
          console.log("Access token refreshed successfully");
        }
      } catch (error) {
        // Refresh token is expired or revoked — force re-login via SessionGuard.
        console.error("Token refresh failed:", error);
        token.error = "RefreshAccessTokenError";
      }

      return token;
    },
    async session({ session, token }) {
      if (token) {
        session.user.id = token.id as string;
        session.user.role = token.role as string;
        session.user.name = token.name as string;
        session.user.email = token.email as string;
        session.user.companyId = (token.companyId as string | null) ?? null;
        session.user.permissions = (token.permissions as string[] | undefined) ?? [];
        session.user.accessToken = token.accessToken as string;
        session.accessToken = token.accessToken as string;
        session.accessTokenExpiry = token.accessTokenExpiry as number | undefined;
        session.error = token.error as "RefreshAccessTokenError" | undefined;
      }
      return session;
    },
  },
});
