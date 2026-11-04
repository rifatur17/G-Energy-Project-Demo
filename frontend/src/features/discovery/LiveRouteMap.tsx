import { useEffect, useRef, useState } from "react";
import L from "leaflet";
import "leaflet/dist/leaflet.css";
import type { DrivingRoute } from "./drivingRoute";
import type { Location } from "./geo";

export function LiveRouteMap({
  origin,
  destination,
  route,
}: {
  origin: Location;
  destination: Location;
  route: DrivingRoute | null;
}) {
  const container = useRef<HTMLDivElement>(null);
  const view = useRef<{
    map: L.Map;
    user: L.CircleMarker;
    path: L.Polyline;
  } | null>(null);
  const [failed, setFailed] = useState(false);
  const { latitude, longitude } = destination;

  useEffect(() => {
    if (!container.current) return;
    let map: L.Map | undefined;
    try {
      map = L.map(container.current, { scrollWheelZoom: false }).setView(
        [latitude, longitude],
        14,
      );
      L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
        maxZoom: 19,
        attribution:
          '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
      })
        .on("tileerror", () => setFailed(true))
        .addTo(map);
      L.circleMarker([latitude, longitude], {
        color: "#047857",
        radius: 9,
        fillOpacity: 1,
      })
        .bindTooltip("Reserved station")
        .addTo(map);
      view.current = {
        map,
        user: L.circleMarker([latitude, longitude], {
          color: "#2563eb",
          radius: 8,
          fillOpacity: 1,
        })
          .bindTooltip("Your location")
          .addTo(map),
        path: L.polyline([], { color: "#1d4ed8", weight: 5 }).addTo(map),
      };
    } catch {
      setFailed(true);
    }
    return () => {
      view.current = null;
      map?.remove();
    };
  }, [latitude, longitude]);

  useEffect(() => {
    view.current?.user.setLatLng([origin.latitude, origin.longitude]);
  }, [origin, latitude, longitude]);

  useEffect(() => {
    const current = view.current;
    if (!current) return;
    current.path.setLatLngs(route?.points ?? []);
    const bounds = L.latLngBounds([
      current.user.getLatLng(),
      L.latLng(latitude, longitude),
    ]);
    if (route) bounds.extend(current.path.getBounds());
    current.map.fitBounds(bounds, { padding: [24, 24], maxZoom: 16 });
  }, [route, latitude, longitude]);

  return (
    <>
      <div
        ref={container}
        className="station-map live-route-map"
        role="region"
        aria-label="Live driving route to reserved station"
      />
      {failed && (
        <p className="hint" role="status">
          Map tiles may be unavailable. Route details are shown below when
          available.
        </p>
      )}
    </>
  );
}
