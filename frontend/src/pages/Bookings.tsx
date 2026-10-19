import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import {
  useChargingApi,
  type Booking,
  type Session,
  remainingSeconds,
} from "../features/charging/api";
import {
  useMobilityApi,
  type Vehicle,
  type Station,
} from "../features/mobility/api";
export function Countdown({ booking }: { booking: Booking }) {
  const [elapsed, setElapsed] = useState(0);
  useEffect(() => {
    const start = performance.now();
    setElapsed(0);
    const timer = setInterval(() => setElapsed(performance.now() - start), 250);
    return () => clearInterval(timer);
  }, [booking]);
  const left = remainingSeconds(
    booking.expiresAtUtc,
    booking.serverTimeUtc,
    elapsed,
  );
  return (
    <span role="timer" aria-label="Reservation countdown">
      {left > 0
        ? `${Math.floor(left / 60)}:${String(left % 60).padStart(2, "0")} remaining`
        : "Reservation time ended · checking server"}
    </span>
  );
}
export function Bookings() {
  const api = useChargingApi(),
    mobility = useMobilityApi(),
    navigate = useNavigate();
  const [params] = useSearchParams();
  const stationId = Number(params.get("stationId")),
    chargerId = Number(params.get("chargerId"));
  const [bookings, setBookings] = useState<Booking[]>([]),
    [sessions, setSessions] = useState<Session[]>([]);
  const [vehicles, setVehicles] = useState<Vehicle[]>([]),
    [station, setStation] = useState<Station | null>(null),
    [vehicleId, setVehicleId] = useState(0);
  const [loading, setLoading] = useState(true),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false),
    [message, setMessage] = useState("");
  const key = useRef(crypto.randomUUID());
  const selectedVehicle = vehicles.find((v) => v.id === vehicleId);
  const charger = station?.chargers.find((c) => c.id === chargerId);
  useEffect(() => {
    key.current = crypto.randomUUID();
  }, [vehicleId, stationId, chargerId]);
  useEffect(() => {
    let active = true,
      pending = false;
    async function load() {
      if (pending) return;
      pending = true;
      try {
        const [b, s] = await Promise.all([api.bookings(), api.sessions()]);
        if (active) {
          setBookings(b);
          setSessions(s);
          setError("");
        }
      } catch (e) {
        if (active) setError((e as Error).message);
      } finally {
        pending = false;
        if (active) setLoading(false);
      }
    }
    void load();
    const timer = setInterval(() => {
      if (!document.hidden) void load();
    }, 3000);
    const focus = () => void load();
    window.addEventListener("focus", focus);
    return () => {
      active = false;
      clearInterval(timer);
      window.removeEventListener("focus", focus);
    };
  }, [api]);
  useEffect(() => {
    let active = true;
    setStation(null);
    setVehicles([]);
    if (stationId && chargerId)
      Promise.all([mobility.vehicles(), mobility.station(stationId)])
        .then(([v, s]) => {
          if (active) {
            setVehicles(v);
            setStation(s);
          }
        })
        .catch((e) => {
          if (active) setError(e.message);
        });
    return () => {
      active = false;
    };
  }, [mobility, stationId, chargerId]);
  async function reserve() {
    if (!selectedVehicle || !charger) return;
    setBusy(true);
    setError("");
    try {
      const current = await api.availability(charger.id);
      if (!current.isAvailable)
        throw new Error(
          `Charger is ${current.effectiveStatus.toLowerCase()}. Choose another charging point.`,
        );
      const b = await api.reserve(
        { vehicleId, stationId, chargerId },
        key.current,
      );
      key.current = crypto.randomUUID();
      setBookings((old) => [b, ...old.filter((x) => x.id !== b.id)]);
      setMessage("Charger reserved. Start charging within three minutes.");
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  async function act(b: Booking, start: boolean) {
    setBusy(true);
    setError("");
    setMessage("");
    try {
      if (start) {
        const s = await api.start(b.id);
        navigate(`/charging-sessions/${s.id}`);
      } else {
        const updated = await api.cancel(b.id);
        setBookings((old) => old.map((x) => (x.id === b.id ? updated : x)));
        setMessage("Reservation released.");
      }
    } catch (e) {
      setError((e as Error).message);
      try {
        const updated = await api.booking(b.id);
        setBookings((old) => old.map((x) => (x.id === b.id ? updated : x)));
      } catch {
        /* Polling retries status. */
      }
    } finally {
      setBusy(false);
    }
  }
  const eligible = vehicles.filter(
    (v) =>
      v.canCharge &&
      v.batteryPercent < 100 &&
      v.connectorType === charger?.connectorType,
  );
  return (
    <section className="mobility-page charging-page">
      <div className="page-heading">
        <div>
          <div className="eyebrow muted">YOUR CHARGING JOURNEY</div>
          <h1>Reserve. Connect. Recharge.</h1>
          <p className="subtitle">
            Three-minute reservations and server-powered demo charging.
          </p>
        </div>
        <Link className="secondary" to="/stations">
          Find a charging point ↗
        </Link>
      </div>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {message && (
        <p className="success panel" role="status">
          {message}
        </p>
      )}
      {stationId > 0 && chargerId > 0 && (
        <section className="panel reservation-form">
          <h2>Confirm your charging point</h2>
          {!station ? (
            <p role="status">Loading charger and eligible vehicles…</p>
          ) : !charger ? (
            <p>Charger not found at this station.</p>
          ) : (
            <>
              <h3>
                {station.name} · {charger.chargerCode}
              </h3>
              <p>{station.address}</p>
              <p>
                {charger.connectorType} · {charger.powerKW} kW ·{" "}
                {charger.pricePerKWh.toFixed(2)} BDT/kWh
              </p>
              {station.isDemo && (
                <p className="hint">
                  Sample location, not a verified real charging station.
                </p>
              )}
              <label htmlFor="booking-vehicle">Vehicle</label>
              <select
                id="booking-vehicle"
                value={vehicleId}
                onChange={(e) => setVehicleId(Number(e.target.value))}
              >
                <option value={0}>Choose an eligible vehicle</option>
                {eligible.map((v) => (
                  <option value={v.id} key={v.id}>
                    {v.registrationNo} · {v.batteryPercent}%
                  </option>
                ))}
              </select>
              {eligible.length === 0 && (
                <p>
                  No compatible vehicle with charging permission and battery
                  below 100%. <Link to="/vehicles">Review vehicles</Link>.
                </p>
              )}
              <button
                className="primary compact"
                disabled={
                  busy ||
                  !selectedVehicle ||
                  !eligible.some((v) => v.id === vehicleId)
                }
                onClick={() => void reserve()}
              >
                {busy ? "Reserving…" : "Reserve for 3 minutes"}
              </button>
            </>
          )}
        </section>
      )}
      <h2>Reservations</h2>
      {loading ? (
        <p role="status">Loading reservations…</p>
      ) : bookings.length === 0 ? (
        <div className="panel empty">
          No reservations yet. Choose a charger from the station map.
        </div>
      ) : (
        <div className="booking-grid">
          {bookings.map((b) => (
            <article className="panel booking-card" key={b.id}>
              <div className="card-top">
                <span className="pill">{b.status}</span>
                {b.status === "Reserved" && <Countdown booking={b} />}
              </div>
              <h3>{b.stationName}</h3>
              <p>
                {b.chargerCode} · Vehicle #{b.vehicleId}
              </p>
              <p className="hint">{b.bookingCode}</p>
              {b.status === "Reserved" ? (
                <div className="action-row">
                  <button
                    className="primary compact"
                    disabled={
                      busy ||
                      remainingSeconds(b.expiresAtUtc, b.serverTimeUtc) === 0
                    }
                    onClick={() => void act(b, true)}
                  >
                    Start charging
                  </button>
                  <button
                    className="secondary"
                    disabled={busy}
                    onClick={() => void act(b, false)}
                  >
                    Cancel reservation
                  </button>
                </div>
              ) : b.status === "Expired" ? (
                <p>Reservation expired. The charger has been released.</p>
              ) : b.sessionId ? (
                <Link to={`/charging-sessions/${b.sessionId}`}>
                  View charging session ↗
                </Link>
              ) : (
                <p>Reservation released.</p>
              )}
            </article>
          ))}
        </div>
      )}
      <h2>Charging history</h2>
      {!loading && sessions.length === 0 && (
        <p className="hint">
          Your sessions will appear here, including sessions resumed after a
          refresh.
        </p>
      )}
      <div className="booking-grid">
        {sessions.map((s) => (
          <Link
            className="panel session-history"
            to={`/charging-sessions/${s.id}`}
            key={s.id}
          >
            <strong>
              Session #{s.id} · {s.status}
            </strong>
            <span>
              {s.batteryPercent.toFixed(2)}% · {s.energyKWh.toFixed(3)} kWh ·{" "}
              {s.cost.toFixed(2)} {s.currency}
            </span>
            <span className="hint">
              {s.paymentStatus} · {new Date(s.startedAtUtc).toLocaleString()}
            </span>
          </Link>
        ))}
      </div>
      <p className="hint">
        Simulation only. No physical EV connection or payment is performed.
      </p>
    </section>
  );
}
