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
import App from "../App";
import { AdminDashboard } from "../pages/AdminDashboard";
import { AdminNetwork } from "../pages/AdminNetwork";
import { AdminMonitor } from "../pages/AdminMonitor";
import { StationEditor, ChargerEditor } from "../features/admin/NetworkForms";
import {
  useAdminApi,
  type AdminStation,
  type AdminCharger,
} from "../features/admin/api";
vi.mock("../features/admin/api", () => ({ useAdminApi: vi.fn() }));
const auth = vi.hoisted(() => ({
  user: {
    id: 1,
    userCode: "GE-ADM-QA",
    role: "Admin",
    fullName: "QA Admin",
    email: "qa@example.test",
    phone: "",
    createdAtUtc: new Date().toISOString(),
  },
  loading: false,
  error: "",
  logout: vi.fn(),
  accessToken: "qa-token",
}));
vi.mock("../features/auth/AuthContext", () => ({ useAuth: () => auth }));
const station: AdminStation = {
  id: 1,
  name: "QA Sample Station",
  address: "Demo location only",
  latitude: 23.8,
  longitude: 90.4,
  isActive: true,
  isDemo: true,
  chargerCount: 1,
  version: "station-v1",
};
const charger: AdminCharger = {
  id: 2,
  stationId: 1,
  stationName: station.name,
  chargerCode: "QA-DC",
  connectorType: "CCS2",
  powerKW: 60,
  pricePerKWh: 25,
  isActive: true,
  configuredStatus: "Reserved",
  effectiveStatus: "Reserved",
  rowVersion: "charger-v1",
};
const calls = {
  dashboard: vi.fn(),
  stations: vi.fn(),
  saveStation: vi.fn(),
  chargers: vi.fn(),
  saveCharger: vi.fn(),
  monitor: vi.fn(),
};
beforeEach(() => {
  vi.clearAllMocks();
  auth.user.role = "Admin";
  calls.dashboard.mockResolvedValue({
    serverTimeUtc: new Date().toISOString(),
    totalUsers: 4,
    activeStations: 1,
    totalChargers: 2,
    availableChargers: 1,
    activeBookings: 1,
    activeSessions: 0,
    completedSessions: 1,
    completedChargingPayments: 1,
    successfulTopUps: 1,
    failedTopUps: 1,
    refundCount: 1,
    grossChargingPayments: 250,
    refunds: 250,
    netChargingPayments: 0,
    totalWalletBalance: 1000,
    currency: "BDT",
    isSimulated: true,
  });
  calls.stations.mockResolvedValue([station]);
  calls.chargers.mockResolvedValue([charger]);
  calls.monitor.mockResolvedValue([]);
  calls.saveStation.mockResolvedValue({ ...station, isActive: false });
  calls.saveCharger.mockResolvedValue(charger);
  vi.mocked(useAdminApi).mockReturnValue(calls);
});
afterEach(cleanup);
it("dashboard renders real API counts and separate gross refund and net simulated totals", async () => {
  render(
    <MemoryRouter>
      <AdminDashboard />
    </MemoryRouter>,
  );
  expect(screen.getByRole("status")).toHaveTextContent("Loading");
  const users = await screen.findByText("Registered users");
  expect(users.closest("article")).toHaveTextContent("4");
  expect(
    screen.getByText("Net charging payments").parentElement,
  ).toHaveTextContent("0.00 BDT");
  expect(
    screen.getByText("Gross charging payments").parentElement,
  ).toHaveTextContent("250.00 BDT");
  expect(screen.getByText(/No real funds were collected/)).toBeInTheDocument();
});
it("dashboard errors expose refresh without inventing statistics", async () => {
  calls.dashboard.mockRejectedValue(new Error("Admin service unavailable"));
  render(
    <MemoryRouter>
      <AdminDashboard />
    </MemoryRouter>,
  );
  expect(await screen.findByRole("alert")).toHaveTextContent("unavailable");
  expect(screen.queryByText("Registered users")).not.toBeInTheDocument();
  calls.dashboard.mockResolvedValueOnce({
    totalUsers: 4,
    currency: "BDT",
    serverTimeUtc: new Date().toISOString(),
  });
  fireEvent.click(screen.getByRole("button", { name: "Refresh overview" }));
  await waitFor(() => expect(calls.dashboard).toHaveBeenCalledTimes(2));
});
it("Owner and Driver routes cannot render administration controls", async () => {
  for (const role of ["CarOwner", "CarDriver"]) {
    auth.user.role = role;
    const view = render(
      <MemoryRouter initialEntries={["/admin/stations"]}>
        <App />
      </MemoryRouter>,
    );
    expect(await screen.findByText("Your workspace")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "New station" }),
    ).not.toBeInTheDocument();
    expect(calls.stations).not.toHaveBeenCalled();
    view.unmount();
  }
});
it("station edits require review before a consequential save", async () => {
  const onSave = vi.fn(),
    user = userEvent.setup();
  render(
    <StationEditor
      current={station}
      busy={false}
      onSave={onSave}
      onCancel={vi.fn()}
    />,
  );
  await user.click(screen.getByLabelText("Station active"));
  await user.click(
    screen.getByRole("button", { name: "Review station changes" }),
  );
  expect(onSave).not.toHaveBeenCalled();
  expect(
    screen.getByRole("group", { name: "Confirm station changes" }),
  ).toHaveTextContent("Inactive");
  await user.click(
    screen.getByRole("button", { name: "Confirm station save" }),
  );
  expect(onSave).toHaveBeenCalledWith(
    expect.objectContaining({ isActive: false, name: station.name }),
  );
});
it("charger review shows tariff and Offline intention before saving", async () => {
  const user = userEvent.setup(),
    onSave = vi.fn();
  render(
    <ChargerEditor
      station={station}
      current={charger}
      busy={false}
      onSave={onSave}
      onCancel={vi.fn()}
    />,
  );
  await user.click(screen.getByLabelText("Charger enabled"));
  await user.click(
    screen.getByRole("button", { name: "Review charger changes" }),
  );
  expect(onSave).not.toHaveBeenCalled();
  expect(
    screen.getByRole("group", { name: "Confirm charger changes" }),
  ).toHaveTextContent("25.00 BDT/kWh · Offline");
  await user.click(
    screen.getByRole("button", { name: "Confirm charger save" }),
  );
  expect(onSave).toHaveBeenCalledWith(
    expect.objectContaining({ isActive: false, pricePerKWh: 25 }),
  );
});
it("network loads actual availability and reports a server activity conflict", async () => {
  const user = userEvent.setup();
  calls.saveCharger.mockRejectedValue(
    new Error("A charger has a valid reservation or active session."),
  );
  render(
    <MemoryRouter>
      <AdminNetwork />
    </MemoryRouter>,
  );
  await user.click(
    await screen.findByRole("button", { name: "Manage chargers · #1" }),
  );
  expect(await screen.findByText("Reserved")).toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: "Edit charger · #2" }));
  await user.click(screen.getByLabelText("Charger enabled"));
  await user.click(
    screen.getByRole("button", { name: "Review charger changes" }),
  );
  await user.click(
    screen.getByRole("button", { name: "Confirm charger save" }),
  );
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "valid reservation",
  );
  expect(calls.saveCharger).toHaveBeenCalledWith(
    1,
    expect.objectContaining({ isActive: false }),
    charger,
  );
});
it("network search empty state and creation use backend APIs", async () => {
  const user = userEvent.setup();
  render(
    <MemoryRouter>
      <AdminNetwork />
    </MemoryRouter>,
  );
  await screen.findByText(station.name);
  calls.stations.mockResolvedValue([]);
  await user.type(screen.getByLabelText("Search stations"), "missing");
  await user.click(screen.getByRole("button", { name: "Search" }));
  expect(await screen.findByText("No matching stations.")).toBeInTheDocument();
  expect(calls.stations).toHaveBeenLastCalledWith("missing", "");
  await user.click(screen.getByRole("button", { name: "New station" }));
  await user.type(screen.getByLabelText("Station name"), "New demo station");
  await user.type(screen.getByLabelText("Address"), "Synthetic location");
  await user.click(
    screen.getByRole("button", { name: "Review station changes" }),
  );
  await user.click(
    screen.getByRole("button", { name: "Confirm station save" }),
  );
  await waitFor(() =>
    expect(calls.saveStation).toHaveBeenCalledWith(
      expect.objectContaining({ name: "New demo station", isDemo: true }),
      undefined,
    ),
  );
});
it("monitoring filters and pagination send server parameters and expose no mutation controls", async () => {
  const user = userEvent.setup();
  const rows = Array.from({ length: 20 }, (_, i) => ({
    id: 30 - i,
    reference: "WT-" + (30 - i),
    ownerUserCode: "GE-OWN-QA",
    type: "ChargingPayment",
    status: "Completed",
    amount: 250,
    description: "Simulated payment",
    createdAtUtc: new Date().toISOString(),
    chargingSessionId: 7,
    originalReference: null,
    refundReference: null,
    ownerCanRefund: true,
  }));
  calls.monitor.mockResolvedValue(rows);
  render(
    <MemoryRouter>
      <AdminMonitor kind="wallet-transactions" />
    </MemoryRouter>,
  );
  expect(await screen.findByText("WT-30")).toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: /refund/i }),
  ).not.toBeInTheDocument();
  await user.selectOptions(
    screen.getByLabelText("Transaction type"),
    "ChargingPayment",
  );
  await user.click(screen.getByRole("button", { name: "Apply filters" }));
  await waitFor(() =>
    expect(calls.monitor).toHaveBeenCalledWith("wallet-transactions", {
      type: "ChargingPayment",
    }),
  );
  calls.monitor.mockResolvedValue([{ ...rows[0], id: 10, reference: "WT-10" }]);
  await user.click(screen.getByRole("button", { name: "Load older records" }));
  expect(await screen.findByText("WT-10")).toBeInTheDocument();
  expect(calls.monitor).toHaveBeenLastCalledWith("wallet-transactions", {
    type: "ChargingPayment",
    before: "11",
  });
});
it("booking history handles loading empty error refresh and persisted session references", async () => {
  const user = userEvent.setup();
  let finish!: (value: unknown[]) => void;
  calls.monitor.mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  render(<MemoryRouter><AdminMonitor kind="bookings" /></MemoryRouter>);
  expect(screen.getByText(/Loading records/)).toBeInTheDocument();
  finish([]);
  expect(await screen.findByText(/No matching records/)).toBeInTheDocument();
  calls.monitor.mockRejectedValueOnce(new Error("History temporarily unavailable"));
  await user.click(screen.getByRole("button", {name: "Refresh records"}));
  expect(await screen.findByText("History temporarily unavailable")).toBeInTheDocument();
  calls.monitor.mockResolvedValueOnce([{id: 153, status: "Consumed", persistedStatus: "Consumed", bookingCode: "GE-BKG-QA", stationId: 118, chargerCode: "QA-DC", sessionId: 82}]);
  await user.click(screen.getByRole("button", {name: "Refresh records"}));
  expect(await screen.findByText("GE-BKG-QA")).toBeInTheDocument();
  expect(screen.getByText("82")).toBeInTheDocument();
  expect(screen.queryByText("History temporarily unavailable")).not.toBeInTheDocument();
  expect(screen.queryByRole("button", {name: /cancel booking|refund|pay now/i})).not.toBeInTheDocument();
});
it("monitoring renders the persisted bill payment reference and refund explanation", async () => {
  calls.monitor.mockResolvedValue([
    {
      id: 7,
      status: "Completed",
      cost: 250,
      paymentStatus: "Paid",
      paymentReference: "WT-PAID",
      settlementNote: "Demo payment refunded; session remains settled.",
      energyKWh: 10,
      batteryPercent: 60,
    },
  ]);
  render(
    <MemoryRouter>
      <AdminMonitor kind="charging-sessions" />
    </MemoryRouter>,
  );
  expect(await screen.findByText("WT-PAID")).toBeInTheDocument();
  expect(screen.getByText("250.00 BDT")).toBeInTheDocument();
  expect(screen.getByText(/Demo payment refunded/)).toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: /pay|refund|complete|stop/i }),
  ).not.toBeInTheDocument();
});

