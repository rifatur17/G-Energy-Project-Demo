import { useMemo, useRef } from "react";
import { useAuth } from "../auth/AuthContext";
import { ApiError, request } from "../../services/api";
export interface AdminStation {
  id: number;
  name: string;
  address: string;
  latitude: number;
  longitude: number;
  isActive: boolean;
  isDemo: boolean;
  chargerCount: number;
  version: string;
}
export type StationInput = Omit<
  AdminStation,
  "id" | "version" | "chargerCount"
>;
export interface AdminCharger {
  id: number;
  stationId: number;
  stationName: string;
  chargerCode: string;
  connectorType: string;
  powerKW: number;
  pricePerKWh: number;
  isActive: boolean;
  configuredStatus: string;
  effectiveStatus: string;
  rowVersion: string;
}
export type ChargerInput = Pick<
  AdminCharger,
  "chargerCode" | "connectorType" | "powerKW" | "pricePerKWh" | "isActive"
>;
export interface AdminSummary {
  serverTimeUtc: string;
  totalUsers: number;
  activeStations: number;
  totalChargers: number;
  availableChargers: number;
  activeBookings: number;
  activeSessions: number;
  completedSessions: number;
  completedChargingPayments: number;
  successfulTopUps: number;
  failedTopUps: number;
  refundCount: number;
  grossChargingPayments: number;
  refunds: number;
  netChargingPayments: number;
  totalWalletBalance: number;
  currency: string;
  isSimulated: boolean;
}
export type AdminRow = Record<string, string | number | boolean | null> & {
  id: number;
};
export type MonitorKind =
  | "bookings"
  | "charging-sessions"
  | "wallet-transactions"
  | "users"
  | "wallets";
export function useAdminApi() {
  const { accessToken, logout } = useAuth();
  const logoutRef = useRef(logout);
  logoutRef.current = logout;
  return useMemo(() => {
    async function call<T>(
      path: string,
      method = "GET",
      body?: unknown,
      version?: string,
    ): Promise<T> {
      try {
        return await request<T>(
          "/api/admin" + path,
          {
            method,
            ...(body === undefined ? {} : { body: JSON.stringify(body) }),
            headers: version ? { "If-Match": `"${version}"` } : {},
          },
          accessToken ?? undefined,
        );
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) logoutRef.current();
        throw e;
      }
    }
    return {
      dashboard: () => call<AdminSummary>("/dashboard"),
      stations: (search = "", active = "", before?: number) =>
        call<AdminStation[]>(
          `/stations?${new URLSearchParams({ search, ...(active ? { active } : {}), ...(before ? { before: String(before) } : {}), take: "20" })}`,
        ),
      saveStation: (body: StationInput, current?: AdminStation) =>
        call<AdminStation>(
          current ? `/stations/${current.id}` : "/stations",
          current ? "PUT" : "POST",
          body,
          current?.version,
        ),
      chargers: (stationId: number, before?: number) =>
        call<AdminCharger[]>(
          `/stations/${stationId}/chargers?take=20${before ? `&before=${before}` : ""}`,
        ),
      saveCharger: (
        stationId: number,
        body: ChargerInput,
        current?: AdminCharger,
      ) =>
        call<AdminCharger>(
          current
            ? `/chargers/${current.id}`
            : `/stations/${stationId}/chargers`,
          current ? "PUT" : "POST",
          body,
          current?.rowVersion,
        ),
      monitor: (kind: MonitorKind, params: Record<string, string>) =>
        call<AdminRow[]>(
          `/${kind}?${new URLSearchParams({ ...params, take: "20" })}`,
        ),
    };
  }, [accessToken]);
}
