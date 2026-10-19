// @vitest-environment jsdom
import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom/vitest";
import { MemoryRouter } from "react-router-dom";
import { Stations } from "../pages/Stations";
import { useMobilityApi } from "../features/mobility/api";
vi.mock("../features/auth/AuthContext", () => ({
  useAuth: () => ({ user: { role: "CarOwner" } }),
}));
const events = vi.hoisted(() => ({ tileerror: null as (() => void) | null }));
vi.mock("../features/mobility/api", async (original) => ({
  ...(await original<typeof import("../features/mobility/api")>()),
  useMobilityApi: vi.fn(),
}));
vi.mock("leaflet", () => {
  const map = {
    setView: vi.fn().mockReturnThis(),
    fitBounds: vi.fn(),
    remove: vi.fn(),
  };
  const group = { addTo: vi.fn().mockReturnThis(), clearLayers: vi.fn() };
  const marker = {
    bindTooltip: vi.fn().mockReturnThis(),
    addTo: vi.fn().mockReturnThis(),
    on: vi.fn().mockReturnThis(),
  };
  const tiles = {
    on: vi.fn(function (name: string, handler: () => void) {
      if (name === "tileerror") events.tileerror = handler;
      return tiles;
    }),
    addTo: vi.fn(),
  };
  return {
    default: {
      map: () => map,
      layerGroup: () => group,
      tileLayer: () => tiles,
      circleMarker: () => marker,
      latLngBounds: () => [],
    },
  };
});
afterEach(cleanup);
it("a tile error leaves the real station list and charger details available", async () => {
  vi.mocked(useMobilityApi).mockReturnValue({
    stations: vi.fn().mockResolvedValue([
      {
        id: 1,
        name: "Sample Dhaka",
        address: "Demo location",
        latitude: 23.8,
        longitude: 90.4,
        isDemo: true,
        chargers: [
          {
            id: 1,
            chargerCode: "DEMO",
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
      },
    ]),
  } as unknown as ReturnType<typeof useMobilityApi>);
  render(
    <MemoryRouter>
      <Stations />
    </MemoryRouter>,
  );
  expect(
    await screen.findByRole("button", { name: "Sample Dhaka" }),
  ).toBeVisible();
  await waitFor(() => expect(events.tileerror).not.toBeNull());
  await act(async () => events.tileerror?.());
  expect(
    screen.getByText(
      "Map tiles are unavailable. All stations and charger details remain in the list.",
    ),
  ).toBeVisible();
  expect(screen.getByText("CCS2 · 60 kW")).toBeVisible();
  expect(screen.getByRole("button", { name: "Sample Dhaka" })).toBeVisible();
});
