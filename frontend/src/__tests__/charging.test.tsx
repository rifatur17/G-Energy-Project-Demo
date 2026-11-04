// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import {
  act,
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import "@testing-library/jest-dom/vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { Bookings, Countdown } from "../pages/Bookings";
import { ChargingSession } from "../pages/ChargingSession";
import {
  remainingSeconds,
  useChargingApi,
  type Booking,
  type Session,
} from "../features/charging/api";
import { useMobilityApi } from "../features/mobility/api";
import { drivingRoute } from "../features/discovery/drivingRoute";
vi.mock("../features/discovery/drivingRoute", async (original) => ({
  ...(await original<typeof import("../features/discovery/drivingRoute")>()),
  drivingRoute: vi.fn(),
}));
vi.mock("../features/discovery/LiveRouteMap", () => ({
  LiveRouteMap: ({
    origin,
  }: {
    origin: { latitude: number; longitude: number };
  }) => (
    <div role="region" aria-label="Live driving route to reserved station">
      {origin.latitude}, {origin.longitude}
    </div>
  ),
}));
vi.mock("../features/charging/api", async (original) => ({
  ...(await original<typeof import("../features/charging/api")>()),
  useChargingApi: vi.fn(),
}));
vi.mock("../features/mobility/api", () => ({ useMobilityApi: vi.fn() }));
vi.mock("../features/auth/AuthContext", () => ({
  useAuth: () => ({ user: { id: 1, role: "CarOwner" } }),
}));
const now = new Date().toISOString();
const booking: Booking = {
  id: 7,
  bookingCode: "GE-BKG-test",
  vehicleId: 1,
  stationId: 2,
  chargerId: 3,
  stationName: "Sample Dhaka",
  chargerCode: "DC-1",
  status: "Reserved",
  createdAtUtc: now,
  expiresAtUtc: new Date(Date.parse(now) + 180000).toISOString(),
  serverTimeUtc: now,
  sessionId: null,
};
const session: Session = {
  id: 9,
  bookingId: 7,
  vehicleId: 1,
  chargerId: 3,
  status: "Active",
  paymentStatus: "Unpaid",
  settlementNote: "Wallet integration pending",
  startedAtUtc: now,
  endedAtUtc: null,
  serverTimeUtc: now,
  batteryPercent: 60,
  estimatedRangeKm: 166.67,
  energyKWh: 10,
  cost: 250,
  elapsedSeconds: 10,
  powerKW: 60,
  tariffPerKWh: 25,
  simulationSpeed: 60,
  isSimulated: true,
  currency: "BDT",
};
const calls = {
  bookings: vi.fn(),
  sessions: vi.fn(),
  booking: vi.fn(),
  reserve: vi.fn(),
  cancel: vi.fn(),
  start: vi.fn(),
  availability: vi.fn(),
  session: vi.fn(),
  stop: vi.fn(),
};
function page(path = "/bookings") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/bookings" element={<Bookings />} />
        <Route path="/charging-sessions/:id" element={<ChargingSession />} />
      </Routes>
    </MemoryRouter>,
  );
}
beforeEach(() => {
  vi.clearAllMocks();
  calls.bookings.mockResolvedValue([]);
  calls.sessions.mockResolvedValue([]);
  calls.reserve.mockResolvedValue(booking);
  calls.cancel.mockResolvedValue({ ...booking, status: "Cancelled" });
  calls.start.mockResolvedValue(session);
  calls.availability.mockResolvedValue({
    isAvailable: true,
    effectiveStatus: "Available",
  });
  calls.session.mockResolvedValue(session);
  calls.stop.mockResolvedValue({
    ...session,
    status: "Completed",
    endedAtUtc: now,
  });
  calls.booking.mockResolvedValue(booking);
  vi.mocked(useChargingApi).mockReturnValue(calls);
  vi.mocked(useMobilityApi).mockReturnValue({
    vehicles: vi.fn().mockResolvedValue([
      {
        id: 1,
        registrationNo: "QA-EV",
        canCharge: true,
        batteryPercent: 40,
        connectorType: "CCS2",
      },
    ]),
    station: vi.fn().mockResolvedValue({
      id: 2,
      name: "Sample Dhaka",
      address: "Demo only",
      latitude: 23.8103,
      longitude: 90.4125,
      isDemo: true,
      chargers: [
        {
          id: 3,
          chargerCode: "DC-1",
          connectorType: "CCS2",
          powerKW: 60,
          pricePerKWh: 25,
        },
      ],
    }),
  } as unknown as ReturnType<typeof useMobilityApi>);
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});
it("creates a real API reservation with a stable idempotency key and displays countdown", async () => {
  const user = userEvent.setup();
  page("/bookings?stationId=2&chargerId=3");
  expect(
    await screen.findByText(
      "No reservations yet. Choose a charger from the station map.",
    ),
  ).toBeInTheDocument();
  await user.selectOptions(await screen.findByLabelText("Vehicle"), "1");
  await user.click(
    screen.getByRole("button", { name: "Reserve for 3 minutes" }),
  );
  expect(await screen.findByRole("timer")).toHaveTextContent("3:00 remaining");
  expect(
    await screen.findByRole("button", { name: "Start live directions" }),
  ).toBeEnabled();
  expect(calls.availability).toHaveBeenCalledWith(3);
  expect(calls.reserve).toHaveBeenCalledWith(
    { vehicleId: 1, stationId: 2, chargerId: 3 },
    expect.any(String),
  );
});
it("loads directions for a saved reservation and retries unavailable station details", async () => {
  calls.bookings.mockResolvedValue([booking]);
  const mobility = vi.mocked(useMobilityApi).getMockImplementation()!();
  vi.mocked(mobility.station).mockRejectedValueOnce(new Error("Offline"));
  page();
  expect(await screen.findByText(/directions are unavailable/)).toBeVisible();
  expect(screen.getByRole("button", { name: "Start charging" })).toBeEnabled();
  await userEvent.click(
    screen.getByRole("button", { name: "Retry directions" }),
  );
  expect(
    await screen.findByRole("button", { name: "Start live directions" }),
  ).toBeEnabled();
  expect(mobility.station).toHaveBeenCalledWith(2);
  expect(
    screen.getByText(/sample pin, not a verified charging station/),
  ).toBeVisible();
});
it("tracks only after opt-in, updates the dot and route details, and stops on cancellation", async () => {
  calls.bookings.mockResolvedValue([booking]);
  let fix: PositionCallback;
  const watchPosition = vi.fn((callback: PositionCallback) => {
    fix = callback;
    return 12;
  });
  const clearWatch = vi.fn();
  const previous = Object.getOwnPropertyDescriptor(navigator, "geolocation");
  Object.defineProperty(navigator, "geolocation", {
    configurable: true,
    value: { watchPosition, clearWatch },
  });
  vi.mocked(drivingRoute).mockResolvedValue({
    distance: 9400,
    duration: 744,
    points: [
      [23.8, 90.4],
      [23.81, 90.41],
    ],
    roads: ["Test Road"],
  });
  try {
    page();
    const start = await screen.findByRole("button", {
      name: "Start live directions",
    });
    expect(watchPosition).not.toHaveBeenCalled();
    await userEvent.click(start);
    expect(watchPosition).toHaveBeenCalledTimes(1);
    act(() =>
      fix({
        coords: { latitude: 23.8, longitude: 90.4 },
      } as GeolocationPosition),
    );
    expect(await screen.findByText(/About 13 min · 9.4 km/)).toBeVisible();
    expect(screen.getByText("Test Road")).toBeInTheDocument();
    expect(drivingRoute).toHaveBeenCalledWith(
      { latitude: 23.8, longitude: 90.4 },
      expect.objectContaining({ id: 2 }),
      expect.any(AbortSignal),
    );
    act(() =>
      fix({
        coords: { latitude: 23.801, longitude: 90.401 },
      } as GeolocationPosition),
    );
    expect(
      screen.getByRole("region", { name: /Live driving route/ }),
    ).toHaveTextContent("23.801, 90.401");
    expect(drivingRoute).toHaveBeenCalledTimes(1);
    const time = vi.spyOn(Date, "now").mockReturnValue(Date.now() + 16000);
    try {
      vi.mocked(drivingRoute).mockResolvedValueOnce({
        distance: 3200,
        duration: 360,
        points: [
          [23.805, 90.405],
          [23.81, 90.41],
        ],
        roads: [],
      });
      act(() =>
        fix({
          coords: { latitude: 23.805, longitude: 90.405 },
        } as GeolocationPosition),
      );
      expect(await screen.findByText(/About 6 min · 3.2 km/)).toBeVisible();
      expect(drivingRoute).toHaveBeenCalledTimes(2);
    } finally {
      time.mockRestore();
    }
    await userEvent.click(
      screen.getByRole("button", { name: "Cancel reservation" }),
    );
    expect(clearWatch).toHaveBeenCalledWith(12);
    expect(
      screen.queryByRole("region", { name: /Live driving route/ }),
    ).not.toBeInTheDocument();
  } finally {
    if (previous) Object.defineProperty(navigator, "geolocation", previous);
    else Reflect.deleteProperty(navigator, "geolocation");
  }
});
it("countdown uses server time rather than the browser wall clock and clamps at zero", () => {
  expect(remainingSeconds(booking.expiresAtUtc, booking.serverTimeUtc)).toBe(
    180,
  );
  expect(
    remainingSeconds(booking.expiresAtUtc, booking.serverTimeUtc, 181000),
  ).toBe(0);
  vi.useFakeTimers();
  render(<Countdown booking={booking} />);
  act(() => vi.advanceTimersByTime(1000));
  expect(screen.getByRole("timer")).toHaveTextContent("2:59 remaining");
  act(() => vi.advanceTimersByTime(180000));
  expect(screen.getByRole("timer")).toHaveTextContent("checking server");
});
it("renders authoritative expiry on return and never offers start", async () => {
  calls.bookings.mockResolvedValue([{ ...booking, status: "Expired" }]);
  page();
  expect(
    await screen.findByText(
      "Reservation expired. The charger has been released.",
    ),
  ).toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Start charging" }),
  ).not.toBeInTheDocument();
});
it("cancels and releases a reservation", async () => {
  calls.bookings.mockResolvedValue([booking]);
  page();
  fireEvent.click(
    await screen.findByRole("button", { name: "Cancel reservation" }),
  );
  await waitFor(() => expect(calls.cancel).toHaveBeenCalledWith(7));
  expect(await screen.findByText("Cancelled")).toBeInTheDocument();
});
it("starts charging, displays server progress, and persists the stop summary", async () => {
  calls.bookings.mockResolvedValue([booking]);
  page();
  fireEvent.click(
    await screen.findByRole("button", { name: "Start charging" }),
  );
  expect(await screen.findByRole("progressbar")).toHaveAttribute("value", "60");
  expect(screen.getByText("166.67 km")).toBeInTheDocument();
  expect(screen.getByText("10.000 kWh")).toBeInTheDocument();
  fireEvent.click(
    screen.getByRole("button", { name: "Stop and finish charging" }),
  );
  expect(await screen.findByText("Session summary")).toBeInTheDocument();
  expect(calls.stop).toHaveBeenCalledWith(9);
  expect(screen.getByText("Payment: Unpaid")).toBeInTheDocument();
  expect(screen.getByText("250.00 BDT")).toBeInTheDocument();
});
it("shows booking API conflicts and retains the key for an uncertain retry", async () => {
  const user = userEvent.setup();
  calls.reserve.mockRejectedValue(
    new Error("Charger was reserved by another customer."),
  );
  page("/bookings?stationId=2&chargerId=3");
  await user.selectOptions(await screen.findByLabelText("Vehicle"), "1");
  await user.click(
    screen.getByRole("button", { name: "Reserve for 3 minutes" }),
  );
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "another customer",
  );
  await user.click(
    screen.getByRole("button", { name: "Reserve for 3 minutes" }),
  );
  expect(calls.reserve.mock.calls[0][1]).toBe(calls.reserve.mock.calls[1][1]);
});
it("shows expired-start conflict and refreshes authoritative status", async () => {
  calls.bookings.mockResolvedValue([booking]);
  calls.start.mockRejectedValue(new Error("Booking expired"));
  calls.booking.mockResolvedValue({ ...booking, status: "Expired" });
  page();
  fireEvent.click(
    await screen.findByRole("button", { name: "Start charging" }),
  );
  expect(
    await screen.findByText(
      "Reservation expired. The charger has been released.",
    ),
  ).toBeInTheDocument();
  expect(screen.getByRole("alert")).toHaveTextContent("expired");
});
it("shows session loading and stop errors without pretending completion", async () => {
  calls.stop.mockRejectedValue(new Error("Please retry finalization"));
  page("/charging-sessions/9");
  expect(screen.getByRole("status")).toHaveTextContent("Loading");
  fireEvent.click(
    await screen.findByRole("button", { name: "Stop and finish charging" }),
  );
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "retry finalization",
  );
  expect(screen.queryByText("Session summary")).not.toBeInTheDocument();
});
