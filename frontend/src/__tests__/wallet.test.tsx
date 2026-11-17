// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import "@testing-library/jest-dom/vitest";
import { MemoryRouter } from "react-router-dom";
import { Wallet } from "../pages/Wallet";
import { SessionPayment } from "../features/wallet/SessionPayment";
import { useWalletApi, type WalletTransaction } from "../features/wallet/api";
import type { Session } from "../features/charging/api";
vi.mock("../features/wallet/api", () => ({ useWalletApi: vi.fn() }));
const auth = vi.hoisted(() => ({ user: { id: 1, role: "CarOwner" } }));
vi.mock("../features/auth/AuthContext", () => ({ useAuth: () => auth }));
const now = new Date().toISOString();
const wallet = {
  id: 1,
  balance: 1000,
  currency: "BDT",
  createdAtUtc: now,
  updatedAtUtc: now,
  maxTransactionAmount: 100000,
  maxBalance: 1000000,
  isDemo: true,
};
const tx: WalletTransaction = {
  id: 10,
  reference: "WT-DEMO-10",
  type: "TopUp",
  status: "Completed",
  amount: 1000,
  currency: "BDT",
  description: "Simulated top-up completed; no real money transferred.",
  chargingSessionId: null,
  originalTransactionId: null,
  createdAtUtc: now,
  completedAtUtc: now,
  canRefund: false,
  refundReference: null,
};
const session = {
  id: 9,
  payerOwnerUserId: 1,
  status: "Completed",
  paymentStatus: "Unpaid",
  cost: 250,
  currency: "BDT",
} as Session;
const calls = {
  wallet: vi.fn(),
  transactions: vi.fn(),
  topUpCheckout: vi.fn(),
  topUps: vi.fn(),
  pay: vi.fn(),
  refund: vi.fn(),
};
const assign = vi.fn();
function page(url = "/wallet") {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <Wallet />
    </MemoryRouter>,
  );
}
beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
  auth.user = { id: 1, role: "CarOwner" };
  calls.wallet.mockResolvedValue(wallet);
  calls.transactions.mockResolvedValue([]);
  calls.topUps.mockResolvedValue([]);
  calls.topUpCheckout.mockResolvedValue({
    tranId: "GWABC",
    redirectUrl: "http://localhost:5080/api/payments/sslcommerz-demo/GWABC",
    gateway: "SSLCommerzDemo",
    amount: 1000,
    currency: "BDT",
  });
  Object.defineProperty(window, "location", {
    configurable: true,
    value: { ...window.location, assign },
  });
  calls.pay.mockResolvedValue({
    transaction: { ...tx, type: "ChargingPayment", amount: 250 },
    balance: 750,
    currency: "BDT",
  });
  calls.refund.mockResolvedValue({
    transaction: { ...tx, reference: "WT-REFUND" },
    balance: 1000,
    currency: "BDT",
  });
  vi.mocked(useWalletApi).mockReturnValue(calls);
});
afterEach(cleanup);
it("loads the real API wallet and renders empty history with demo warning", async () => {
  page();
  expect(screen.getByRole("status")).toHaveTextContent("Loading wallet");
  expect(
    await screen.findByLabelText("Available wallet balance"),
  ).toHaveTextContent("1000.00 BDT");
  expect(screen.getByText(/No transactions yet/)).toBeInTheDocument();
  expect(screen.getByText(/Never enter real card numbers/)).toBeInTheDocument();
  expect(calls.wallet).toHaveBeenCalledTimes(1);
});
it("rejects invalid top-up amounts before opening a checkout", async () => {
  page();
  await screen.findByLabelText("Amount (BDT)");
  fireEvent.change(screen.getByLabelText("Amount (BDT)"), {
    target: { value: "-1" },
  });
  fireEvent.click(
    screen.getByRole("button", { name: "Add money with SSLCommerz" }),
  );
  expect(await screen.findByRole("alert")).toHaveTextContent("positive amount");
  expect(calls.topUpCheckout).not.toHaveBeenCalled();
});
it("top-up opens the SSLCommerz checkout instead of adding money directly", async () => {
  const user = userEvent.setup();
  page();
  await user.type(await screen.findByLabelText("Amount (BDT)"), "1000");
  await user.click(
    screen.getByRole("button", { name: "Add money with SSLCommerz" }),
  );
  await waitFor(() =>
    expect(assign).toHaveBeenCalledWith(
      "http://localhost:5080/api/payments/sslcommerz-demo/GWABC",
    ),
  );
  expect(calls.topUpCheckout).toHaveBeenCalledWith(1000);
  expect(screen.queryByLabelText(/Simulated payment outcome/)).toBeNull();
});
it("reports the checkout result and lists recent top-up attempts", async () => {
  calls.topUps.mockResolvedValue([
    {
      id: 3,
      tranId: "GWXYZ",
      gateway: "SSLCommerzDemo",
      amount: 500,
      status: "Failed",
      method: "BKASH-BKash",
      failureReason:
        "Payment service is not available at the moment (demo checkout).",
      createdAtUtc: now,
      completedAtUtc: now,
    },
  ]);
  page("/wallet?topup=unavailable");
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "This payment service is not available at the moment",
  );
  expect(await screen.findByText(/GWXYZ/)).toBeInTheDocument();
  expect(screen.getByText(/SSLCommerz \(demo\) · bKash/)).toBeInTheDocument();
  cleanup();
  page("/wallet?topup=paid");
  expect(await screen.findByRole("status")).toHaveTextContent(
    "added to your wallet",
  );
});
it("history displays an eligible full refund and retains original reference", async () => {
  const payment = {
    ...tx,
    type: "ChargingPayment",
    chargingSessionId: 9,
    canRefund: true,
  };
  calls.transactions.mockResolvedValue([payment]);
  page();
  fireEvent.click(
    await screen.findByRole("button", { name: "Refund full demo payment" }),
  );
  expect(await screen.findByRole("status")).toHaveTextContent(
    "Original payment retained",
  );
  expect(calls.refund).toHaveBeenCalledWith(10);
});
it("insufficient wallet balance stays unpaid and provides a top-up link", async () => {
  calls.pay.mockRejectedValue(
    new Error(
      "Insufficient wallet balance. Top up and pay this completed session.",
    ),
  );
  const onPaid = vi.fn();
  render(
    <MemoryRouter>
      <SessionPayment session={session} onPaid={onPaid} />
    </MemoryRouter>,
  );
  fireEvent.click(screen.getByRole("button", { name: "Pay from wallet" }));
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Insufficient wallet balance",
  );
  expect(
    screen.getByRole("link", { name: "Check balance / add money ↗" }),
  ).toHaveAttribute("href", "/wallet");
  expect(onPaid).not.toHaveBeenCalled();
});
it("successful settlement uses only session ID and reports the payment reference", async () => {
  const onPaid = vi.fn();
  render(
    <MemoryRouter>
      <SessionPayment session={session} onPaid={onPaid} />
    </MemoryRouter>,
  );
  fireEvent.click(screen.getByRole("button", { name: "Pay from wallet" }));
  await waitFor(() => expect(onPaid).toHaveBeenCalledWith("WT-DEMO-10"));
  expect(calls.pay).toHaveBeenCalledWith(9);
});
it("already-paid sessions and Drivers cannot offer a wallet debit", () => {
  const { rerender } = render(
    <MemoryRouter>
      <SessionPayment
        session={{ ...session, paymentStatus: "Paid" }}
        onPaid={vi.fn()}
      />
    </MemoryRouter>,
  );
  expect(screen.queryByRole("button")).not.toBeInTheDocument();
  auth.user = { id: 2, role: "CarDriver" };
  rerender(
    <MemoryRouter>
      <SessionPayment session={session} onPaid={vi.fn()} />
    </MemoryRouter>,
  );
  expect(screen.queryByRole("button")).not.toBeInTheDocument();
});
