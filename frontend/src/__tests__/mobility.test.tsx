// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import "@testing-library/jest-dom/vitest";
import { MemoryRouter } from "react-router-dom";
import App from "../App";
import { AuthProvider } from "../features/auth/AuthContext";
import { api, type User } from "../services/api";
import {
  useMobilityApi,
  type Vehicle,
  type Station,
} from "../features/mobility/api";
import { distanceKm, demoLocations } from "../features/discovery/geo";
vi.mock("../services/api", async (original) => ({
  ...(await original<typeof import("../services/api")>()),
  api: { me: vi.fn(), login: vi.fn(), register: vi.fn() },
}));
vi.mock("../features/mobility/api", async (original) => ({
  ...(await original<typeof import("../features/mobility/api")>()),
  useMobilityApi: vi.fn(),
}));
vi.mock("../features/discovery/StationMap", () => ({
  StationMap: () => <div aria-label="Station map">Interactive map</div>,
}));
const owner: User = {
  id: 1,
  userCode: "GE-OWN-001001",
  fullName: "Demo Owner",
  email: "owner@example.test",
  phone: "01700000000",
  role: "CarOwner",
  createdAtUtc: new Date().toISOString(),
};
const vehicle: Vehicle = {
  id: 1,
  registrationNo: "DHAKA AB-1234",
  model: "Demo EV",
  connectorType: "CCS2",
  batteryCapacityKWh: 50,
  batteryPercent: 40,
  consumptionKWhPerKm: 0.18,
  estimatedRangeKm: 111.11,
  isSimulated: true,
  canCharge: true,
  rowVersion: "AQ==",
  createdAtUtc: new Date().toISOString(),
  updatedAtUtc: new Date().toISOString(),
};
const station: Station = {
  id: 1,
  name: "G Energy Demo — Dhaka",
  address: "Sample site; not verified",
  latitude: 23.8103,
  longitude: 90.4125,
  isDemo: true,
  chargers: [
    {
      id: 1,
      chargerCode: "DEMO-DC",
      connectorType: "CCS2",
      powerKW: 60,
      pricePerKWh: 25,
      currency: "BDT",
      configuredStatus: "Available",
      effectiveStatus: "Available",
      isAvailable: true,
      availabilitySource: "ConfiguredStatus",
    },
  ],
};
const client = {
  vehicles: vi.fn(),
  vehicle: vi.fn(),
  create: vi.fn(),
  update: vi.fn(),
  drivers: vi.fn(),
  lookup: vi.fn(),
  assign: vi.fn(),
  permission: vi.fn(),
  revoke: vi.fn(),
  stations: vi.fn(),
  station: vi.fn(),
};
function show(path = "/vehicles") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <App />
      </AuthProvider>
    </MemoryRouter>,
  );
}
beforeEach(() => {
  vi.resetAllMocks();
  sessionStorage.setItem(
    "g-energy-session",
    JSON.stringify({
      accessToken: "test-token",
      expiresAtUtc: new Date(Date.now() + 1800000).toISOString(),
      role: "Admin",
    }),
  );
  vi.mocked(api.me).mockResolvedValue(owner);
  vi.mocked(useMobilityApi).mockReturnValue(client);
  client.vehicles.mockResolvedValue([]);
  client.vehicle.mockResolvedValue(vehicle);
  client.drivers.mockResolvedValue([]);
  client.stations.mockResolvedValue([station]);
});
afterEach(() => {
  cleanup();
  sessionStorage.clear();
  vi.unstubAllGlobals();
});
describe("Member 2 screens", () => {
  it("shows a loading state until the vehicle response arrives", async () => {
    let resolve!: (value: Vehicle[]) => void;
    client.vehicles.mockReturnValue(
      new Promise<Vehicle[]>((done) => {
        resolve = done;
      }),
    );
    show();
    expect(await screen.findByText("Loading your vehicles…")).toBeVisible();
    await act(async () => resolve([]));
    expect(await screen.findByText("Your garage is ready")).toBeVisible();
  });
  it("adds an owner vehicle and refreshes the garage", async () => {
    const user = userEvent.setup();
    show();
    expect(await screen.findByText("Your garage is ready")).toBeVisible();
    await user.click(screen.getByRole("button", { name: "+ Add vehicle" }));
    await user.type(
      screen.getByLabelText("Registration number"),
      "DHAKA AB-1234",
    );
    await user.type(screen.getByLabelText("Vehicle model"), "Demo EV");
    client.create.mockResolvedValue(vehicle);
    client.vehicles.mockResolvedValue([vehicle]);
    await user.click(screen.getByRole("button", { name: "Add vehicle" }));
    expect(await screen.findByRole("link", { name: "Demo EV" })).toBeVisible();
    expect(client.create).toHaveBeenCalledWith(
      expect.objectContaining({
        batteryCapacityKWh: 50,
        batteryPercent: 40,
        consumptionKWhPerKm: 0.18,
      }),
    );
    expect(screen.getByText("Estimated range")).toBeVisible();
  });
  it("shows assigned vehicles without owner mutation controls", async () => {
    vi.mocked(api.me).mockResolvedValue({ ...owner, role: "CarDriver" });
    client.vehicles.mockResolvedValue([{ ...vehicle, canCharge: false }]);
    show();
    expect(await screen.findByText("View access only")).toBeVisible();
    expect(
      screen.queryByRole("button", { name: "+ Add vehicle" }),
    ).not.toBeInTheDocument();
  });
  it("reports loading/errors and supports retry", async () => {
    client.vehicles
      .mockRejectedValueOnce(new Error("Vehicle service unavailable"))
      .mockResolvedValue([]);
    show();
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Vehicle service unavailable",
    );
    await userEvent
      .setup()
      .click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByText("Your garage is ready")).toBeVisible();
  });
  it("lets the owner find, assign, change permission and revoke a driver", async () => {
    const driver = {
      driverUserId: 2,
      userCode: "GE-DRV-001002",
      fullName: "Demo Driver",
      canCharge: true,
      isActive: true,
    };
    const user = userEvent.setup();
    client.lookup.mockResolvedValue(driver);
    client.assign.mockResolvedValue(undefined);
    client.permission.mockResolvedValue(undefined);
    client.revoke.mockResolvedValue(undefined);
    show("/vehicles/1");
    await screen.findByRole("button", { name: "Find driver" });
    await user.type(screen.getByLabelText("Driver User ID"), driver.userCode);
    await user.click(screen.getByRole("button", { name: "Find driver" }));
    await screen.findByText("Demo Driver");
    await user.click(
      screen.getByRole("checkbox", {
        name: "Allow charging with this vehicle",
      }),
    );
    client.drivers.mockResolvedValue([driver]);
    await user.click(screen.getByRole("button", { name: "Assign driver" }));
    expect(
      await screen.findByRole("button", { name: "Disable charging" }),
    ).toBeVisible();
    expect(client.assign).toHaveBeenCalledWith(vehicle, driver.userCode, true);
    client.drivers.mockResolvedValue([{ ...driver, canCharge: false }]);
    await user.click(screen.getByRole("button", { name: "Disable charging" }));
    expect(
      await screen.findByRole("button", { name: "Enable charging" }),
    ).toBeVisible();
    client.drivers.mockResolvedValue([]);
    await user.click(screen.getByRole("button", { name: "Revoke" }));
    expect(await screen.findByText("No drivers assigned yet.")).toBeVisible();
    expect(client.revoke).toHaveBeenCalled();
  });
  it("edits simulated fields and preserves errors on failed saves", async () => {
    const user = userEvent.setup();
    client.update.mockRejectedValueOnce(
      new Error("Vehicle changed. Reload and try again."),
    );
    show("/vehicles/1");
    await user.click(
      await screen.findByRole("button", { name: "Edit vehicle" }),
    );
    await user.clear(screen.getByLabelText("Simulated battery (%)"));
    await user.type(screen.getByLabelText("Simulated battery (%)"), "65");
    await user.click(screen.getByRole("button", { name: "Save changes" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Vehicle changed. Reload and try again.",
    );
    expect(client.update).toHaveBeenCalledWith(
      vehicle,
      expect.objectContaining({ batteryPercent: 65 }),
    );
  });
  it("uses explicit demo origins, connector filtering and a list-only view", async () => {
    const user = userEvent.setup();
    show("/stations");
    expect(
      await screen.findByRole("button", { name: station.name }),
    ).toBeVisible();
    expect(screen.getByText("≈ 0.0 km")).toBeVisible();
    expect(screen.getByText("DEMO-DC · 25.00 BDT/kWh")).toBeVisible();
    await user.selectOptions(screen.getByLabelText("Connector"), "CCS2");
    await waitFor(() =>
      expect(client.stations).toHaveBeenLastCalledWith("CCS2", ""),
    );
    await user.selectOptions(
      screen.getByLabelText("Starting location"),
      "Feni",
    );
    expect(
      await screen.findByRole("button", { name: station.name }),
    ).toBeVisible();
    await user.click(screen.getByRole("button", { name: "List only" }));
    expect(screen.queryByLabelText("Station map")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: station.name })).toBeVisible();
  });
  it("keeps discovery usable when browser location is denied", async () => {
    const user = userEvent.setup();
    const getCurrentPosition = vi.fn((_success, error) => error({ code: 1 }));
    vi.stubGlobal(
      "navigator",
      Object.assign(Object.create(navigator), {
        geolocation: { getCurrentPosition },
      }),
    );
    show("/stations");
    await screen.findByRole("button", { name: station.name });
    expect(getCurrentPosition).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Use my location" }));
    expect(
      await screen.findByText(
        "Location was denied, unavailable or timed out. Choose a demo origin below.",
      ),
    ).toBeVisible();
    expect(screen.getByRole("button", { name: station.name })).toBeVisible();
  });
  it("shows station error and empty states", async () => {
    client.stations
      .mockRejectedValueOnce(new Error("Station service unavailable"))
      .mockResolvedValue([]);
    show("/stations");
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Station service unavailable",
    );
    await userEvent
      .setup()
      .click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByText("No matching stations")).toBeVisible();
  });
  it("calculates Haversine straight-line distance symmetrically", () => {
    expect(distanceKm(demoLocations[0], demoLocations[0])).toBe(0);
    const km = distanceKm(demoLocations[0], demoLocations[4]);
    expect(km).toBeGreaterThan(210);
    expect(km).toBeLessThan(220);
    expect(distanceKm(demoLocations[4], demoLocations[0])).toBeCloseTo(km, 8);
  });
});
