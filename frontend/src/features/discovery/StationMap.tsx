import { useEffect, useRef, useState } from "react";
import L from "leaflet";
import "leaflet/dist/leaflet.css";
import type { Station } from "../mobility/api";
import type { Location } from "./geo";
export function StationMap({
  stations,
  origin,
  selected,
  onSelect,
}: {
  stations: Station[];
  origin: Location;
  selected: number | null;
  onSelect: (id: number) => void;
}) {
  const container = useRef<HTMLDivElement>(null);
  const map = useRef<L.Map | null>(null);
  const layer = useRef<L.LayerGroup | null>(null);
  const callback = useRef(onSelect);
  callback.current = onSelect;
  const [failed, setFailed] = useState(false);
  const [loaded, setLoaded] = useState(false);
  useEffect(() => {
    if (!container.current) return;
    let active = true;
    let instance: L.Map | null = null;
    try {
      instance = L.map(container.current, { scrollWheelZoom: false }).setView(
        [23.15, 91.05],
        7,
      );
      map.current = instance;
      layer.current = L.layerGroup().addTo(instance);
      const tiles = L.tileLayer(
        "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
        {
          maxZoom: 19,
          attribution:
            '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
        },
      );
      tiles.on("tileerror", () => {
        if (active) setFailed(true);
      });
      tiles.on("load", () => {
        if (active) setLoaded(true);
      });
      tiles.addTo(instance);
    } catch {
      setFailed(true);
    }
    return () => {
      active = false;
      instance?.remove();
      map.current = null;
      layer.current = null;
    };
  }, []);
  useEffect(() => {
    const group = layer.current;
    const instance = map.current;
    if (!group || !instance) return;
    group.clearLayers();
    const points: L.LatLngTuple[] = [[origin.latitude, origin.longitude]];
    L.circleMarker([origin.latitude, origin.longitude], {
      radius: 7,
      color: "#2563eb",
      fillOpacity: 1,
    })
      .bindTooltip("Selected starting location")
      .addTo(group);
    stations.forEach((s) => {
      points.push([s.latitude, s.longitude]);
      const marker = L.circleMarker([s.latitude, s.longitude], {
        radius: s.id === selected ? 12 : 8,
        color: s.id === selected ? "#063f30" : "#0a8055",
        fillColor: "#59c98b",
        fillOpacity: 1,
        weight: 3,
      });
      const text = document.createElement("span");
      text.textContent = s.name;
      marker
        .bindTooltip(text, {
          permanent:
            stations.length <= 5 &&
            (container.current?.clientWidth ?? 0) >= 500,
          direction: "top",
        })
        .on("click", () => callback.current(s.id))
        .addTo(group);
    });
    if (points.length > 1)
      instance.fitBounds(L.latLngBounds(points), {
        padding: [38, 38],
        maxZoom: 11,
      });
    else instance.setView(points[0], 9);
  }, [stations, origin, selected]);
  return (
    <section className="map-panel" aria-label="Station map">
      <div
        ref={container}
        className="station-map"
        role="region"
        aria-label="Interactive map of sample charging locations"
      />
      {failed ? (
        <p className="map-message" role="status">
          Map tiles are unavailable. All stations and charger details remain in
          the list.
        </p>
      ) : !loaded ? (
        <p className="map-message" role="status">
          Loading map tiles. You can use the station list now.
        </p>
      ) : null}
      <p className="map-caption">
        Sample locations · Blue marker: your selected origin ·{" "}
        <a
          href="https://www.openstreetmap.org/copyright"
          target="_blank"
          rel="noreferrer"
        >
          © OpenStreetMap contributors
        </a>
      </p>
    </section>
  );
}
