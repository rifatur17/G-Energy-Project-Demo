import { useMemo, useRef } from "react";
import { useAuth } from "../auth/AuthContext";
import { ApiError, request } from "../../services/api";
export const connectors = ["CCS2", "Type2", "CHAdeMO"] as const;
export interface VehicleInput {
  registrationNo: string;
  model: string;
  connectorType: string;
  batteryCapacityKWh: number;
  batteryPercent: number;
  consumptionKWhPerKm: number;
}
export interface Vehicle extends VehicleInput {
  id: number;
  estimatedRangeKm: number;
  isSimulated: boolean;
  canCharge: boolean;
  rowVersion: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}
export interface Driver {
  driverUserId: number;
  userCode: string;
  fullName: string;
  canCharge: boolean;
  isActive: boolean;
}
export interface Charger {
  id: number;
  chargerCode: string;
  connectorType: string;
  powerKW: number;
  pricePerKWh: number;
  currency: string;
  configuredStatus: string;
  effectiveStatus: string;
  isAvailable: boolean;
  availabilitySource: string;
}
export interface Station {
  id: number;
  name: string;
  address: string;
  latitude: number;
  longitude: number;
  isDemo: boolean;
  chargers: Charger[];
}
export function useMobilityApi() {
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
          path,
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
      vehicles: () => call<Vehicle[]>("/api/vehicles"),
      vehicle: (id: number) => call<Vehicle>(`/api/vehicles/${id}`),
      create: (body: VehicleInput) =>
        call<Vehicle>("/api/vehicles", "POST", body),
      update: (v: Vehicle, body: VehicleInput) =>
        call<Vehicle>(`/api/vehicles/${v.id}`, "PUT", body, v.rowVersion),
      drivers: (id: number) => call<Driver[]>(`/api/vehicles/${id}/drivers`),
      lookup: (code: string) =>
        call<Driver>(
          `/api/drivers/lookup?userCode=${encodeURIComponent(code.trim())}`,
        ),
      assign: (v: Vehicle, code: string, canCharge: boolean) =>
        call<void>(
          `/api/vehicles/${v.id}/drivers`,
          "POST",
          { userCode: code, canCharge },
          v.rowVersion,
        ),
      permission: (v: Vehicle, d: Driver, canCharge: boolean) =>
        call<void>(
          `/api/vehicles/${v.id}/drivers/${d.driverUserId}`,
          "PUT",
          { canCharge },
          v.rowVersion,
        ),
      revoke: (v: Vehicle, d: Driver) =>
        call<void>(
          `/api/vehicles/${v.id}/drivers/${d.driverUserId}`,
          "DELETE",
          undefined,
          v.rowVersion,
        ),
      stations: (connector: string, search: string) =>
        call<Station[]>(
          `/api/stations?${new URLSearchParams({ ...(connector ? { connectorType: connector } : {}), ...(search ? { search } : {}) })}`,
        ),
      station: (id: number) => call<Station>(`/api/stations/${id}`),
    };
  }, [accessToken]);
}
