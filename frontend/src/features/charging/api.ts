import { useMemo, useRef } from "react";
import { useAuth } from "../auth/AuthContext";
import { ApiError, request } from "../../services/api";
export interface Booking {
  id: number;
  bookingCode: string;
  vehicleId: number;
  stationId: number;
  chargerId: number;
  stationName: string;
  chargerCode: string;
  status: string;
  createdAtUtc: string;
  expiresAtUtc: string;
  serverTimeUtc: string;
  sessionId: number | null;
}
export interface Session {
  id: number;
  bookingId: number;
  vehicleId: number;
  chargerId: number;
  status: string;
  paymentStatus: string;
  settlementNote: string;
  payerOwnerUserId?: number;
  paymentReference?: string | null;
  startedAtUtc: string;
  endedAtUtc: string | null;
  serverTimeUtc: string;
  batteryPercent: number;
  estimatedRangeKm: number;
  energyKWh: number;
  cost: number;
  elapsedSeconds: number;
  powerKW: number;
  tariffPerKWh: number;
  simulationSpeed: number;
  isSimulated: boolean;
  currency: string;
}
export interface Availability {
  chargerId: number;
  effectiveStatus: string;
  isAvailable: boolean;
  serverTimeUtc: string;
}
export function remainingSeconds(
  expires: string,
  serverTime: string,
  elapsedMs = 0,
) {
  return Math.max(
    0,
    Math.ceil(
      (Date.parse(expires) - Date.parse(serverTime) - elapsedMs) / 1000,
    ),
  );
}
export function useChargingApi() {
  const { accessToken, logout } = useAuth();
  const logoutRef = useRef(logout);
  logoutRef.current = logout;
  return useMemo(() => {
    async function call<T>(
      path: string,
      method = "GET",
      body?: unknown,
      key?: string,
    ): Promise<T> {
      try {
        return await request<T>(
          path,
          {
            method,
            ...(body === undefined ? {} : { body: JSON.stringify(body) }),
            headers: key ? { "Idempotency-Key": key } : {},
          },
          accessToken ?? undefined,
        );
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) logoutRef.current();
        throw e;
      }
    }
    return {
      bookings: () => call<Booking[]>("/api/bookings"),
      booking: (id: number) => call<Booking>(`/api/bookings/${id}`),
      reserve: (
        body: { vehicleId: number; stationId: number; chargerId: number },
        key: string,
      ) => call<Booking>("/api/bookings", "POST", body, key),
      cancel: (id: number) =>
        call<Booking>(`/api/bookings/${id}/cancel`, "POST"),
      start: (id: number) => call<Session>(`/api/bookings/${id}/start`, "POST"),
      availability: (id: number) =>
        call<Availability>(`/api/chargers/${id}/availability`),
      sessions: () => call<Session[]>("/api/charging-sessions"),
      session: (id: number) => call<Session>(`/api/charging-sessions/${id}`),
      stop: (id: number) =>
        call<Session>(`/api/charging-sessions/${id}/stop`, "POST"),
    };
  }, [accessToken]);
}
