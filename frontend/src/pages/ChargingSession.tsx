import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useChargingApi, type Session } from "../features/charging/api";
export function ChargingSession() {
  const api = useChargingApi(),
    { id } = useParams();
  const [session, setSession] = useState<Session | null>(null),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false),
    [loading, setLoading] = useState(true),
    [retry, setRetry] = useState(0);
  useEffect(() => {
    let active = true,
      pending = false;
    async function load() {
      if (pending) return;
      pending = true;
      try {
        const s = await api.session(Number(id));
        if (active) {
          setSession(s);
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
    window.addEventListener("focus", load);
    return () => {
      active = false;
      clearInterval(timer);
      window.removeEventListener("focus", load);
    };
  }, [api, id, retry]);
  async function stop() {
    if (!session) return;
    setBusy(true);
    setError("");
    try {
      setSession(await api.stop(session.id));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <section className="mobility-page charging-page">
      <div className="page-heading">
        <div>
          <div className="eyebrow muted">SIMULATED CHARGING</div>
          <h1>
            {session?.status === "Completed"
              ? "Your charge is complete."
              : "A little charge. More possibilities."}
          </h1>
          <p className="subtitle">
            Server-calculated energy, battery and cost. No charging hardware
            connected.
          </p>
        </div>
        <Link to="/bookings" className="secondary">
          All reservations ↗
        </Link>
      </div>
      {error && (
        <div className="panel">
          <p className="error" role="alert">
            {error}
          </p>
          <button className="secondary" onClick={() => setRetry((r) => r + 1)}>
            Retry status
          </button>
        </div>
      )}
      {loading ? (
        <p role="status">Loading charging session…</p>
      ) : (
        session && (
          <article className="panel session-panel">
            <div className="card-top">
              <span className="pill">{session.status}</span>
              <span>
                Session #{session.id} · Vehicle #{session.vehicleId}
              </span>
            </div>
            <div className="battery-reading">
              <strong>
                {session.batteryPercent.toFixed(2)}
                <small>%</small>
              </strong>
              <span>Battery level</span>
            </div>
            <progress
              max={100}
              value={session.batteryPercent}
              aria-label="Vehicle battery"
            />
            <div className="session-stats">
              <div>
                <strong>{session.estimatedRangeKm.toFixed(2)} km</strong>
                <span>Estimated range</span>
              </div>
              <div>
                <strong>{session.energyKWh.toFixed(3)} kWh</strong>
                <span>Energy delivered</span>
              </div>
              <div>
                <strong>
                  {session.cost.toFixed(2)} {session.currency}
                </strong>
                <span>
                  {session.status === "Active"
                    ? "Estimated cost"
                    : "Final cost"}
                </span>
              </div>
              <div>
                <strong>
                  {Math.floor(session.elapsedSeconds / 60)}m{" "}
                  {Math.floor(session.elapsedSeconds % 60)}s
                </strong>
                <span>Elapsed real time</span>
              </div>
            </div>
            <p>
              {session.powerKW} kW · {session.tariffPerKWh.toFixed(2)}{" "}
              {session.currency}/kWh · {session.simulationSpeed}× demo speed
            </p>
            {session.status === "Active" ? (
              <>
                <p role="status">
                  Charging in progress. Progress continues if you leave this
                  page or restart the server.
                </p>
                <button
                  className="primary compact"
                  disabled={busy}
                  onClick={() => void stop()}
                >
                  {busy ? "Stopping…" : "Stop and finish charging"}
                </button>
              </>
            ) : (
              <>
                <h2>Session summary</h2>
                <p>
                  Finished{" "}
                  {session.endedAtUtc
                    ? new Date(session.endedAtUtc).toLocaleString()
                    : ""}
                  . Final battery and cost are saved.
                </p>
              </>
            )}
            <p className="settlement-note">
              <strong>Payment: {session.paymentStatus}</strong> ·{" "}
              {session.settlementNote}.{" "}
              {session.paymentStatus === "Unpaid" &&
                "No wallet charge has been made."}
            </p>
          </article>
        )
      )}
    </section>
  );
}
