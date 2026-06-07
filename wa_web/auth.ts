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

// How many seconds before expiry to proactively refresh
const REFRESH_BUFFER_SECONDS = 60;

declare module "next-auth" {
  interface User {
    id: string;
    role: string;
    name: string | null;
    email?: string | null;
    companyId?: string | null; // null for SuperAdmin; set for tenant users
    accessToken?: string; // JWT token from wa_api
    accessTokenExpiry?: number; // Unix timestamp (ms)
  }
  interface Session {
    user: {
      id: string;
      name: string | null;
      email?: string | null;
      role: string;
      companyId?: string | null;
      accessToken?: string; // JWT token from wa_api
    };
    accessToken?: string; // JWT token from wa_api
    accessTokenExpiry?: number; // Unix timestamp (ms)
    error?: "RefreshAccessTokenError";
  }
  interface JWT {
    accessToken?: string; // JWT token from wa_api
  }
}

export const { handlers, signIn, signOut, auth } = NextAuth({
  ...authConfig,
  session: {
    strategy: "jwt",
    maxAge: 8 * 60 * 60, // 8 hours — matches wa_api access-token lifetime
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
          // Validate input
          if (!credentials?.email || !credentials?.password) {
            return null;
          }

          const email = (credentials.email as string).trim();
          const password = credentials.password as string;

          if (email.length < 1 || password.length < 1) {
            return null;
          }

          // Call wa_api: POST /api/v1/auth/login → { success, data: { accessToken, expiresAt, user } }
          const response = await apiClient.post(
            `${env.API_URL}/api/v1/auth/login`,
            { email, password },
            { headers: { "Content-Type": "application/json" } },
          );

          // wa_api wraps everything in ApiResponse<T>; success login => success:true
          if (!response.data?.success || !response.data?.data) {
            console.error("Login failed: unexpected response shape");
            return null;
          }

          const data = response.data.data; // { accessToken, expiresAt, user }
          const user = data.user;           // { id, email, fullName, role, companyId }

          if (process.env.NODE_ENV === "development") {
            console.log("Login successful for user:", user.id, user.role);
          }

          return {
            id: String(user.id),
            email: user.email,
            name: user.fullName,
            role: user.role,
            companyId: user.companyId ?? null,
            accessToken: data.accessToken,
            accessTokenExpiry: data.expiresAt
              ? new Date(data.expiresAt).getTime()
              : Date.now() + 8 * 60 * 60 * 1000, // fallback: 8 hours (matches API default)
          };
        } catch (error) {
          console.error("Auth error:", error);

          // Log API error response if available
          if (error && typeof error === "object" && "response" in error) {
            const axiosError = error as any;
            if (axiosError.response?.data) {
              console.error(
                "API Error Response:",
                JSON.stringify(axiosError.response.data, null, 2),
              );
            }
          }

          return null;
        }
      },
    }),
  ],
  callbacks: {
    ...authConfig.callbacks,
    async jwt({ token, user }) {
      // On sign in, add user data and API token to the JWT.
      if (user) {
        token.id = user.id;
        token.role = user.role;
        token.name = user.name;
        token.email = user.email;
        token.companyId = user.companyId;
        token.accessToken = user.accessToken;
        token.accessTokenExpiry = user.accessTokenExpiry;
        token.error = undefined;
        return token;
      }

      // wa_api issues stateless access tokens with NO refresh endpoint.
      // Once the access token expires, the session is unrecoverable — flag it
      // so SessionGuard signs the user out and forces a fresh login.
      const expiresAt = token.accessTokenExpiry as number | undefined;
      const isExpired = !expiresAt || Date.now() >= expiresAt - REFRESH_BUFFER_SECONDS * 1000;
      if (isExpired) {
        token.error = "RefreshAccessTokenError";
      }

      return token;
    },
    async session({ session, token }) {
      // Copy user data from token to session.
      if (token) {
        session.user.id = token.id as string;
        session.user.role = token.role as string;
        session.user.name = token.name as string;
        session.user.email = token.email as string;
        session.user.companyId = (token.companyId as string | null) ?? null;
        session.user.accessToken = token.accessToken as string;
        session.accessToken = token.accessToken as string;
        session.accessTokenExpiry = token.accessTokenExpiry as number | undefined;
        session.error = token.error as "RefreshAccessTokenError" | undefined;
      }
      return session;
    },
  },
});
