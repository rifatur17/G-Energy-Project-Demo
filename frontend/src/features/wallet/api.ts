import { useMemo, useRef } from "react";
import { useAuth } from "../auth/AuthContext";
import { ApiError, request } from "../../services/api";
export interface Wallet {
  id: number;
  balance: number;
  currency: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  maxTransactionAmount: number;
  maxBalance: number;
  isDemo: boolean;
}
export interface WalletTransaction {
  id: number;
  reference: string;
  type: string;
  status: string;
  amount: number;
  currency: string;
  description: string;
  chargingSessionId: number | null;
  originalTransactionId: number | null;
  createdAtUtc: string;
  completedAtUtc: string;
  canRefund: boolean;
  refundReference: string | null;
}
export interface WalletOperation {
  transaction: WalletTransaction;
  balance: number;
  currency: string;
}
export interface TopUpCheckout {
  tranId: string;
  redirectUrl: string;
  gateway: string;
  amount: number;
  currency: string;
}
export interface TopUpAttempt {
  id: number;
  tranId: string;
  gateway: string;
  amount: number;
  status: "Initiated" | "Paid" | "Failed" | "Cancelled";
  method: string | null;
  failureReason: string | null;
  createdAtUtc: string;
  completedAtUtc: string | null;
}
export function useWalletApi() {
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
      wallet: () => call<Wallet>("/api/wallet"),
      transactions: (before?: number) =>
        call<WalletTransaction[]>(
          `/api/wallet/transactions?take=20${before ? `&before=${before}` : ""}`,
        ),
      /** Opens an SSLCommerz checkout; the wallet is credited after the gateway confirms. */
      topUpCheckout: (amount: number) =>
        call<TopUpCheckout>("/api/wallet/top-up/checkout", "POST", { amount }),
      topUps: () => call<TopUpAttempt[]>("/api/wallet/top-ups"),
      pay: (id: number) =>
        call<WalletOperation>(`/api/charging-sessions/${id}/pay`, "POST"),
      refund: (id: number) =>
        call<WalletOperation>(`/api/wallet/transactions/${id}/refund`, "POST"),
    };
  }, [accessToken]);
}
