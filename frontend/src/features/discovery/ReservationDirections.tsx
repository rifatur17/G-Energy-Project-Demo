import { useEffect, useState } from "react";
import type { Station } from "../mobility/api";

export function ReservationDirections({
  stationId,
  loadStation,
}: {
  stationId: number;
  loadStation: (id: number) => Promise<Station>;
}) {
  const [destination, setDestination] = useState<Station | null>(null);
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let active = true;
    void loadStation(stationId)
      .then((station) => {
        if (!active) return;
        if (
          station.id !== stationId ||
          !Number.isFinite(station.latitude) ||
          !Number.isFinite(station.longitude) ||
          Math.abs(station.latitude) > 90 ||
          Math.abs(station.longitude) > 180
        ) {
          setFailed(true);
          return;
        }
        setDestination(station);
      })
      .catch(() => { if (active) setFailed(true); });
    return () => { active = false; };
  }, [stationId, loadStation, attempt]);

  if (failed) return (
    <div>
      <p className="hint" role="status">Station directions are unavailable right now.</p>
      <button className="secondary" onClick={() => { setFailed(false); setAttempt((value) => value + 1); }}>
        Retry directions
      </button>
    </div>
  );
  if (destination?.id !== stationId) return <p className="hint" role="status">Loading station directions…</p>;

  // Omit the origin so Maps can use the device location or ask for a starting point.
  const params = new URLSearchParams({
    api: "1",
    destination: `${destination.latitude},${destination.longitude}`,
    travelmode: "driving",
  });
  return (
    <div>
      <a className="secondary" href={`https://www.google.com/maps/dir/?${params}`} target="_blank" rel="noopener noreferrer">
        Directions in Google Maps ↗
      </a>
      <p className="hint">
        Opens driving directions and an estimated travel time in Google Maps. If your location is unavailable, enter a starting point there. Directions do not extend the three-minute reservation.
        {destination.isDemo && " This is a sample pin, not a verified charging station."}
      </p>
    </div>
  );
}
