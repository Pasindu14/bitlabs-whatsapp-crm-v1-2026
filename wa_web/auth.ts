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
    accessToken?: string; // JWT token from .NET Core API
    refreshToken?: string; // Refresh token from .NET Core API
    accessTokenExpiry?: number; // Unix timestamp (ms)
  }
  interface Session {
    user: {
      id: string;
      name: string | null;
      email?: string | null;
      role: string;
      accessToken?: string; // JWT token from .NET Core API
    };
    accessToken?: string; // JWT token from .NET Core API
    accessTokenExpiry?: number; // Unix timestamp (ms)
    error?: "RefreshAccessTokenError";
  }
  interface JWT {
    accessToken?: string; // JWT token from .NET Core API
  }
}

export const { handlers, signIn, signOut, auth } = NextAuth({
  ...authConfig,
  session: {
    strategy: "jwt",
    maxAge: 24 * 60 * 60, // 24 hours
  },
  providers: [
    Credentials({
      name: "credentials",
      credentials: {
        username: { label: "Username", type: "text" },
        password: { label: "Password", type: "password" },
        deviceId: { label: "Device ID", type: "text" },
      },
      async authorize(credentials) {
        try {
          // Validate input
          if (!credentials?.username || !credentials?.password) {
            return null;
          }

          const username = (credentials.username as string).trim();
          const password = credentials.password as string;

          // Basic username validation
          if (username.length < 1 || password.length < 1) {
            return null;
          }

          // Call external API
          const response = await apiClient.post(
            `${env.SFA_API_DOMAIN}/api/v1/auth/login`,
            {
              username,
              password,
              deviceId: "test-device-001",
            },
            {
              headers: {
                "Content-Type": "application/json",
              },
            },
          );

          if (!response.data) {
            console.error("Login failed: No data in response");
            return null;
          }

          // Check if the response indicates an error
          if (response.data.error || response.data.message) {
            console.error(
              "Login API returned error:",
              response.data.error || response.data.message,
            );
            return null;
          }

          // Return user object in NextAuth format
          // The API response should include a token field with the JWT
          const data = response.data.data;
          const user = data.user;

          if (process.env.NODE_ENV === "development") {
            console.log("Login successful for user:", user.id);
          }

          return {
            id: String(user.id),
            email: user.email,
            name: user.name,
            role: user.role,
            accessToken: data.accessToken,
            refreshToken: data.refreshToken,
            accessTokenExpiry: data.accessTokenExpiry
              ? new Date(data.accessTokenExpiry).getTime()
              : Date.now() + 5 * 60 * 60 * 1000, // fallback: 5 hours
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
      // On sign in, add user data and API token to JWT
      if (user) {
        token.id = user.id;
        token.role = user.role;
        token.name = user.name;
        token.email = user.email;
        token.accessToken = user.accessToken;
        token.refreshToken = user.refreshToken;
        token.accessTokenExpiry = user.accessTokenExpiry;
        token.error = undefined;
        return token;
      }

      // On subsequent calls, check if the access token is still valid
      const expiresAt = token.accessTokenExpiry as number | undefined;
      const isExpiredOrExpiringSoon =
        !expiresAt ||
        Date.now() >= expiresAt - REFRESH_BUFFER_SECONDS * 1000;

      if (!isExpiredOrExpiringSoon) {
        return token;
      }

      // Access token expired or expiring soon — try to refresh
      try {
        const response = await apiClient.post(
          `${env.SFA_API_DOMAIN}/api/v1/auth/refresh`,
          {
            refreshToken: token.refreshToken,
            deviceId: "test-device-001",
          },
          {
            headers: { "Content-Type": "application/json" },
          },
        );

        const data = response.data?.data;
        if (!data?.accessToken) {
          throw new Error("Refresh response missing accessToken");
        }

        token.accessToken = data.accessToken;
        token.refreshToken = data.refreshToken ?? token.refreshToken;
        token.accessTokenExpiry = data.accessTokenExpiry
          ? new Date(data.accessTokenExpiry).getTime()
          : Date.now() + 5 * 60 * 60 * 1000;
        token.error = undefined;
      } catch {
        token.error = "RefreshAccessTokenError";
      }

      return token;
    },
    async session({ session, token }) {
      // Copy all user data from token to session
      if (token) {
        session.user.id = token.id as string;
        session.user.role = token.role as string;
        session.user.name = token.name as string;
        session.user.email = token.email as string;
        session.user.accessToken = token.accessToken as string;
        session.accessToken = token.accessToken as string;
        session.accessTokenExpiry = token.accessTokenExpiry as number | undefined;
        session.error = token.error as "RefreshAccessTokenError" | undefined;
      }
      return session;
    },
  },
});
