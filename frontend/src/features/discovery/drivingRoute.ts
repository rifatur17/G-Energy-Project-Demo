import type { Location } from "./geo";

export type DrivingRoute = {
  distance: number;
  duration: number;
  points: [number, number][];
  roads: string[];
};

export function isLocation(point: Location) {
  return (
    Number.isFinite(point.latitude) &&
    Math.abs(point.latitude) <= 90 &&
    Number.isFinite(point.longitude) &&
    Math.abs(point.longitude) <= 180
  );
}

let lastRequest = -Infinity;
export async function drivingRoute(
  from: Location,
  to: Location,
  signal: AbortSignal,
): Promise<DrivingRoute> {
  if (Date.now() - lastRequest < 1100)
    throw new Error("Route requests are too close together.");
  lastRequest = Date.now();
  const coordinates = `${from.longitude.toFixed(5)},${from.latitude.toFixed(5)};${to.longitude.toFixed(5)},${to.latitude.toFixed(5)}`;
  const response = await fetch(
    `https://routing.openstreetmap.de/routed-car/route/v1/driving/${coordinates}?overview=full&geometries=geojson&steps=true`,
    {
      signal,
      credentials: "omit",
      cache: "no-store",
    },
  );
  if (!response.ok) throw new Error("Routing service unavailable.");
  const data = await response.json();
  const route = data.code === "Ok" ? data.routes?.[0] : undefined;
  const coordinatesResult = route?.geometry?.coordinates;
  if (
    !Number.isFinite(route?.distance) ||
    route.distance < 0 ||
    !Number.isFinite(route.duration) ||
    route.duration < 0 ||
    !Array.isArray(coordinatesResult) ||
    coordinatesResult.length < 2 ||
    !coordinatesResult.every(
      (point) =>
        Array.isArray(point) &&
        isLocation({ longitude: point[0], latitude: point[1] }),
    )
  ) {
    throw new Error("No driving route was returned.");
  }
  const roads: string[] = [];
  for (const leg of Array.isArray(route.legs) ? route.legs : []) {
    for (const step of Array.isArray(leg.steps) ? leg.steps : []) {
      const name = typeof step.name === "string" ? step.name.trim() : "";
      if (name && name !== roads[roads.length - 1]) roads.push(name);
    }
  }
  return {
    distance: route.distance,
    duration: route.duration,
    points: coordinatesResult.map(([lon, lat]) => [lat, lon]),
    roads,
  };
}
