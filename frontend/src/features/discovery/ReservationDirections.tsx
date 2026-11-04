import { useEffect, useState } from "react";
import type { Station } from "../mobility/api";
import { drivingRoute, isLocation, type DrivingRoute } from "./drivingRoute";
import { distanceKm, type Location } from "./geo";
import { LiveRouteMap } from "./LiveRouteMap";

export function ReservationDirections({
  stationId,
  loadStation,
  expiresAtUtc,
  serverTimeUtc,
}: {
  stationId: number;
  loadStation: (id: number) => Promise<Station>;
  expiresAtUtc: string;
  serverTimeUtc: string;
}) {
  const [destination, setDestination] = useState<Station | null>(null);
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const [tracking, setTracking] = useState(false);
  const [origin, setOrigin] = useState<Location | null>(null);
  const [route, setRoute] = useState<DrivingRoute | null>(null);
  const [issue, setIssue] = useState("");
  const [updating, setUpdating] = useState(false);
  const [expired, setExpired] = useState(false);

  useEffect(() => {
    const remaining = Date.parse(expiresAtUtc) - Date.parse(serverTimeUtc);
    const timer = window.setTimeout(
      () => {
        setExpired(true);
        setTracking(false);
        setOrigin(null);
        setRoute(null);
        setUpdating(false);
      },
      Math.max(0, remaining),
    );
    return () => window.clearTimeout(timer);
  }, [expiresAtUtc, serverTimeUtc]);

  useEffect(() => {
    let active = true;
    void loadStation(stationId)
      .then((station) => {
        if (!active) return;
        if (station.id !== stationId || !isLocation(station)) {
          setFailed(true);
          return;
        }
        setDestination(station);
      })
      .catch(() => {
        if (active) setFailed(true);
      });
    return () => {
      active = false;
    };
  }, [stationId, loadStation, attempt]);

  useEffect(() => {
    if (!tracking || !destination) return;
    let active = true;
    let request: AbortController | null = null;
    let last: Location | null = null;
    let lastAt = 0;
    const geo = navigator.geolocation;
    if (!geo) {
      setIssue(
        "Live location requires a supported browser and HTTPS or localhost.",
      );
      setTracking(false);
      return;
    }
    const watch = geo.watchPosition(
      ({ coords }) => {
        if (!active) return;
        const point = {
          latitude: coords.latitude,
          longitude: coords.longitude,
        };
        if (!isLocation(point)) return;
        setOrigin(point);
        const now = Date.now();
        // The dot updates at GPS speed; request a new route only after meaningful movement.
        if (
          request ||
          (lastAt && now - lastAt < 15000) ||
          (last && distanceKm(last, point) < 0.05)
        )
          return;
        last = point;
        lastAt = now;
        request = new AbortController();
        const signal = AbortSignal.any([
          request.signal,
          AbortSignal.timeout(10000),
        ]);
        setUpdating(true);
        setIssue("");
        void drivingRoute(point, destination, signal)
          .then((result) => {
            if (active) setRoute(result);
          })
          .catch(() => {
            if (active) {
              last = null;
              setRoute(null);
              setIssue(
                "Driving route unavailable. Live location can still update. To retry now, stop and start live directions again.",
              );
            }
          })
          .finally(() => {
            request = null;
            if (active) setUpdating(false);
          });
      },
      (error) => {
        if (!active) return;
        setIssue(
          error.code === 1
            ? "Location permission was denied. Allow location in your browser and try again."
            : "Current location is unavailable. Check your device location settings and try again.",
        );
        setTracking(false);
        setOrigin(null);
        setRoute(null);
        setUpdating(false);
      },
      { enableHighAccuracy: true, maximumAge: 5000, timeout: 15000 },
    );
    return () => {
      active = false;
      geo.clearWatch(watch);
      request?.abort();
    };
  }, [tracking, destination]);

  if (expired) return null;

  if (failed)
    return (
      <div>
        <p className="hint" role="status">
          Station directions are unavailable right now.
        </p>
        <button
          className="secondary"
          onClick={() => {
            setFailed(false);
            setAttempt((value) => value + 1);
          }}
        >
          Retry directions
        </button>
      </div>
    );
  if (destination?.id !== stationId)
    return (
      <p className="hint" role="status">
        Loading station directions…
      </p>
    );

  return (
    <section
      className="reservation-directions"
      aria-label="Directions to reserved station"
    >
      <button
        className="secondary"
        onClick={() => {
          setOrigin(null);
          setRoute(null);
          setIssue("");
          setUpdating(false);
          setTracking(!tracking);
        }}
      >
        {tracking ? "Stop live directions" : "Start live directions"}
      </button>
      <p className="hint">
        Starting shares your position and the station pin with FOSSGIS for
        routing; its service logs route requests. G Energy does not save your
        location. Directions do not extend the three-minute reservation.
        {destination.isDemo &&
          " This is a sample pin, not a verified charging station."}
      </p>
      {tracking && !origin && <p role="status">Waiting for your location…</p>}
      {tracking && origin && (
        <LiveRouteMap origin={origin} destination={destination} route={route} />
      )}
      {updating && (
        <p role="status">
          {route ? "Updating driving route…" : "Calculating driving route…"}
        </p>
      )}
      {issue && <p role="status">{issue}</p>}
      {route && (
        <div aria-live="polite">
          <p>
            <strong>
              About {Math.max(1, Math.ceil(route.duration / 60))} min ·{" "}
              {(route.distance / 1000).toFixed(1)} km
            </strong>{" "}
            by road
          </p>
          <p className="hint">
            Estimated drive time, without live traffic. Blue dot: you · Green
            dot: station.
          </p>
          {route.roads.length > 0 && (
            <details>
              <summary>Roads on this route</summary>
              <ol>
                {route.roads.map((road, index) => (
                  <li key={`${index}-${road}`}>{road}</li>
                ))}
              </ol>
            </details>
          )}
        </div>
      )}
      {tracking && (
        <p className="hint">
          Routing by{" "}
          <a href="https://routing.openstreetmap.de/about.html">
            FOSSGIS / OSRM
          </a>{" "}
          ·{" "}
          <a href="https://www.openstreetmap.org/copyright">
            © OpenStreetMap contributors
          </a>{" "}
          · <a href="https://www.openstreetmap.org/fixthemap">Fix the map</a>
        </p>
      )}
    </section>
  );
}
