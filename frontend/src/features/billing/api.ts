import { useMemo, useRef } from "react";
import { useAuth } from "../auth/AuthContext";
import { ApiError, request } from "../../services/api";
export interface BillingSummary {
  unbilledAmount: number;
  unbilledSessions: number;
  activeSessions: number;
  planFeeDue: number;
  outstandingAmount: number;
  overdueStatements: number;
  vehicles: number;
  vehiclesOnPlan: number;
  planFeePerVehicle: number;
  freeMonths: number;
  nextTrialEndsUtc: string | null;
  dueDays: number;
  canCloseMonth: boolean;
  gateway: string;
  currency: string;
}
export interface BillingLine {
  sessionId: number;
  startedAtUtc: string;
  endedAtUtc: string | null;
  stationName: string;
  chargerCode: string;
  vehicleRegistration: string;
  chargedBy: string;
  energyKWh: number;
  cost: number;
}
export interface Statement {
  id: number;
  statementNumber: string;
  periodKey: string;
  periodStartUtc: string;
  periodEndUtc: string;
  issuedAtUtc: string;
  dueAtUtc: string;
  sessionCount: number;
  chargingTotal: number;
  planVehicleCount: number;
  planFee: number;
  total: number;
  status: "Due" | "Overdue" | "Paid";
  paidAtUtc: string | null;
  paymentReference: string | null;
  currency: string;
}
export interface StatementPayment {
  id: number;
  tranId: string;
  gateway: string;
  amount: number;
  status: "Initiated" | "Paid" | "Failed" | "Cancelled";
  method: string | null;
  bankTranId: string | null;
  failureReason: string | null;
  createdAtUtc: string;
  completedAtUtc: string | null;
}
export interface StatementDetail {
  statement: Statement;
  lines: BillingLine[];
  payments: StatementPayment[];
}
export interface Checkout {
  tranId: string;
  redirectUrl: string;
  gateway: string;
  amount: number;
  currency: string;
}
export function useBillingApi() {
  const { accessToken, logout } = useAuth();
  const logoutRef = useRef(logout);
  logoutRef.current = logout;
  return useMemo(() => {
    async function call<T>(path: string, method = "GET"): Promise<T> {
      try {
        return await request<T>(path, { method }, accessToken ?? undefined);
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) logoutRef.current();
        throw e;
      }
    }
    return {
      summary: () => call<BillingSummary>("/api/billing/summary"),
      unbilled: () => call<BillingLine[]>("/api/billing/unbilled"),
      statements: () => call<Statement[]>("/api/billing/statements"),
      statement: (id: number) =>
        call<StatementDetail>(`/api/billing/statements/${id}`),
      closeMonth: () =>
        call<Statement>("/api/billing/statements/close", "POST"),
      checkout: (id: number) =>
        call<Checkout>(`/api/billing/statements/${id}/checkout`, "POST"),
    };
  }, [accessToken]);
}
/** "BKASH-BKash" → "bKash", "VISA-Dutch Bangla" → "VISA (Dutch Bangla)". */
export function methodLabel(method: string | null) {
  if (!method) return "";
  const [code, name] = method.split("-");
  if (/bkash/i.test(code)) return "bKash";
  if (/nagad/i.test(code)) return "Nagad";
  if (/dbblmobile/i.test(code)) return "Rocket";
  return name ? `${code} (${name})` : code;
}
