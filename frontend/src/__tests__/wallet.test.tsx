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
  topUp: vi.fn(),
  pay: vi.fn(),
  refund: vi.fn(),
};
function page() {
  return render(
    <MemoryRouter>
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
  calls.topUp.mockResolvedValue({
    transaction: tx,
    balance: 1000,
    currency: "BDT",
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
  expect(screen.getByText(/Never enter card/)).toBeInTheDocument();
  expect(calls.wallet).toHaveBeenCalledTimes(1);
});
it("rejects invalid top-up amounts before any API payment", async () => {
  page();
  await screen.findByLabelText("Amount (BDT)");
  fireEvent.change(screen.getByLabelText("Amount (BDT)"), {
    target: { value: "-1" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Add simulated funds" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("positive amount");
  expect(calls.topUp).not.toHaveBeenCalled();
});
it("successful top-up refreshes balance and the persisted transaction reference", async () => {
  const user = userEvent.setup();
  page();
  await user.type(await screen.findByLabelText("Amount (BDT)"), "1000");
  calls.transactions.mockResolvedValue([tx]);
  await user.click(screen.getByRole("button", { name: "Add simulated funds" }));
  expect(await screen.findByRole("status")).toHaveTextContent(
    "No real money transferred",
  );
  expect(calls.topUp).toHaveBeenCalledWith(1000, "Success", expect.any(String));
  expect(await screen.findByText(/WT-DEMO-10 ·/)).toBeInTheDocument();
  expect(calls.wallet).toHaveBeenCalledTimes(2);
});
it("mock failure displays failed status without claiming a credit", async () => {
  const user = userEvent.setup();
  calls.topUp.mockResolvedValue({
    transaction: {
      ...tx,
      status: "Failed",
      description: "Simulated payment failed; no credit applied.",
    },
    balance: 1000,
    currency: "BDT",
  });
  calls.transactions.mockResolvedValue([{ ...tx, status: "Failed" }]);
  page();
  await user.type(await screen.findByLabelText("Amount (BDT)"), "100");
  await user.selectOptions(
    screen.getByLabelText("Simulated payment outcome"),
    "Failure",
  );
  await user.click(screen.getByRole("button", { name: "Add simulated funds" }));
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "no credit applied",
  );
  expect(screen.getByText("Failed")).toBeInTheDocument();
  expect(screen.getByLabelText("Available wallet balance")).toHaveTextContent(
    "1000.00",
  );
});
it("pending top-up disables duplicates and an uncertain retry keeps the same key", async () => {
  let reject!: (error: Error) => void;
  calls.topUp.mockReturnValueOnce(
    new Promise((_, r) => {
      reject = r;
    }),
  );
  page();
  fireEvent.change(await screen.findByLabelText("Amount (BDT)"), {
    target: { value: "1000" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Add simulated funds" }));
  expect(
    await screen.findByRole("button", { name: "Processing…" }),
  ).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "Processing…" }));
  expect(calls.topUp).toHaveBeenCalledTimes(1);
  reject(new Error("Network outcome uncertain"));
  await screen.findByRole("alert");
  fireEvent.click(screen.getByRole("button", { name: "Add simulated funds" }));
  await waitFor(() => expect(calls.topUp).toHaveBeenCalledTimes(2));
  expect(calls.topUp.mock.calls[0][2]).toBe(calls.topUp.mock.calls[1][2]);
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
  fireEvent.click(screen.getByRole("button", { name: "Pay from demo wallet" }));
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Insufficient wallet balance",
  );
  expect(
    screen.getByRole("link", { name: "Check balance / add demo funds ↗" }),
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
  fireEvent.click(screen.getByRole("button", { name: "Pay from demo wallet" }));
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

it("refresh restores an uncertain top-up with the same wallet-scoped key", async () => {
  sessionStorage.setItem(
    "g-energy-wallet-pending-1",
    JSON.stringify({ amount: "125", outcome: "Success", key: "persisted-key" }),
  );
  page();
  expect(await screen.findByLabelText("Amount (BDT)")).toHaveValue(125);
  fireEvent.click(screen.getByRole("button", { name: "Add simulated funds" }));
  await waitFor(() =>
    expect(calls.topUp).toHaveBeenCalledWith(125, "Success", "persisted-key"),
  );
  await waitFor(() =>
    expect(sessionStorage.getItem("g-energy-wallet-pending-1")).toBeNull(),
  );
});
