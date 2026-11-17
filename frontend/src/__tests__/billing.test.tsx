// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import "@testing-library/jest-dom/vitest";
import { MemoryRouter } from "react-router-dom";
import { Billing } from "../pages/Billing";
import {
  useBillingApi,
  type BillingSummary,
  type Statement,
} from "../features/billing/api";
vi.mock("../features/billing/api", async (original) => ({
  ...(await original<typeof import("../features/billing/api")>()),
  useBillingApi: vi.fn(),
}));
const now = new Date().toISOString();
const summary: BillingSummary = {
  unbilledAmount: 450,
  unbilledSessions: 2,
  activeSessions: 0,
  planFeeDue: 0,
  outstandingAmount: 0,
  overdueStatements: 0,
  vehicles: 1,
  vehiclesOnPlan: 0,
  planFeePerVehicle: 200,
  freeMonths: 3,
  nextTrialEndsUtc: now,
  dueDays: 7,
  canCloseMonth: true,
  gateway: "SSLCommerz demo checkout",
  currency: "BDT",
};
const statement: Statement = {
  id: 5,
  statementNumber: "GE-ST-202611-001001-01",
  periodKey: "2026-11",
  periodStartUtc: now,
  periodEndUtc: now,
  issuedAtUtc: now,
  dueAtUtc: now,
  sessionCount: 2,
  chargingTotal: 450,
  planVehicleCount: 0,
  planFee: 0,
  total: 450,
  status: "Due",
  paidAtUtc: null,
  paymentReference: null,
  currency: "BDT",
};
const calls = {
  summary: vi.fn(),
  unbilled: vi.fn(),
  statements: vi.fn(),
  statement: vi.fn(),
  closeMonth: vi.fn(),
  checkout: vi.fn(),
};
const assign = vi.fn();
function page(url = "/billing") {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <Billing />
    </MemoryRouter>,
  );
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(useBillingApi).mockReturnValue(
    calls as unknown as ReturnType<typeof useBillingApi>,
  );
  calls.summary.mockResolvedValue(summary);
  calls.unbilled.mockResolvedValue([]);
  calls.statements.mockResolvedValue([]);
  calls.statement.mockResolvedValue({ statement, lines: [], payments: [] });
  Object.defineProperty(window, "location", {
    configurable: true,
    value: { ...window.location, assign },
  });
});
afterEach(cleanup);

it("shows postpaid totals and never offers a wallet top-up", async () => {
  page();
  expect(
    await screen.findByLabelText("Charges not yet billed"),
  ).toHaveTextContent("৳450.00");
  expect(screen.getByText(/wallet could not cover/)).toBeVisible();
  expect(screen.queryByText(/top-up|add funds/i)).not.toBeInTheDocument();
});

it("issues a statement and opens the SSLCommerz checkout to pay it", async () => {
  calls.closeMonth.mockResolvedValue(statement);
  calls.statements.mockResolvedValueOnce([]).mockResolvedValue([statement]);
  calls.checkout.mockResolvedValue({
    tranId: "GEABC",
    redirectUrl: "https://sandbox.sslcommerz.com/EasyCheckOut/abc",
    gateway: "SSLCommerz",
    amount: 450,
    currency: "BDT",
  });
  page();
  await userEvent.click(
    await screen.findByRole("button", { name: "Issue statement now (demo)" }),
  );
  expect(await screen.findByRole("status")).toHaveTextContent(
    "GE-ST-202611-001001-01 issued for ৳450.00",
  );
  await userEvent.click(
    await screen.findByRole("button", { name: "Pay ৳450.00 with SSLCommerz" }),
  );
  await waitFor(() =>
    expect(assign).toHaveBeenCalledWith(
      "https://sandbox.sslcommerz.com/EasyCheckOut/abc",
    ),
  );
  expect(calls.checkout).toHaveBeenCalledWith(5);
});

it("reports the gateway result when SSLCommerz sends the customer back", async () => {
  page("/billing?payment=paid&statement=5");
  expect(await screen.findByRole("status")).toHaveTextContent(
    "Payment confirmed by SSLCommerz",
  );
  cleanup();
  page("/billing?payment=cancelled&statement=5");
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "cancelled the SSLCommerz checkout",
  );
  cleanup();
  page("/billing?payment=unavailable&statement=5");
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "This payment service is not available at the moment. Nothing was charged",
  );
});
