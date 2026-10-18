export type Role = "Admin" | "CarOwner" | "CarDriver";
export interface User {
  id: number;
  userCode: string;
  fullName: string;
  email: string;
  phone: string;
  role: Role;
  createdAtUtc: string;
}
export interface Registration {
  fullName: string;
  email: string;
  phone: string;
  password: string;
  role: "CarOwner" | "CarDriver";
}
export interface Session {
  accessToken: string;
  userCode: string;
  role: Role;
  expiresAtUtc: string;
}
export class ApiError extends Error {
  status: number;
  constructor(message: string, status: number) {
    super(message);
    this.status = status;
  }
}
const base = (
  import.meta.env.VITE_API_BASE_URL || "http://localhost:5080"
).replace(/\/$/, "");
export async function request<T>(
  path: string,
  options: RequestInit = {},
  token?: string,
): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${base}${path}`, {
      ...options,
      headers: {
        "Content-Type": "application/json",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...options.headers,
      },
    });
  } catch {
    throw new ApiError(
      "Cannot reach G Energy. Check your connection and try again.",
      0,
    );
  }
  const body = await response.json().catch(() => null);
  if (!response.ok) {
    const errors = body?.errors
      ? Object.values(body.errors).flat().join(" ")
      : "";
    throw new ApiError(
      errors ||
        body?.title ||
        (response.status === 429
          ? "Too many attempts. Please wait a minute."
          : "Request failed. Please try again."),
      response.status,
    );
  }
  return body as T;
}
export const api = {
  register: (data: Registration) =>
    request<User>("/api/auth/register", {
      method: "POST",
      body: JSON.stringify(data),
    }),
  login: (userCode: string, password: string) =>
    request<Session>("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ userCode, password }),
    }),
  me: (token: string) => request<User>("/api/auth/me", {}, token),
};
export const dashboardPath = (role: Role) =>
  ({
    Admin: "/dashboard/admin",
    CarOwner: "/dashboard/owner",
    CarDriver: "/dashboard/driver",
  })[role];
