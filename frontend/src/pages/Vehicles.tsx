import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../features/auth/AuthContext";
import {
  useMobilityApi,
  type Vehicle,
  type VehicleInput,
} from "../features/mobility/api";
import { VehicleForm } from "../features/mobility/VehicleForm";
export function Vehicles() {
  const { user } = useAuth();
  const api = useMobilityApi();
  const [vehicles, setVehicles] = useState<Vehicle[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [adding, setAdding] = useState(false);
  const [busy, setBusy] = useState(false);
  const [term, setTerm] = useState("");
  const owner = user?.role === "CarOwner";
  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    api
      .vehicles()
      .then((v) => {
        if (active) setVehicles(v);
      })
      .catch((e) => {
        if (active) setError(e.message);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [api, refresh]);
  async function create(input: VehicleInput) {
    setBusy(true);
    try {
      await api.create(input);
      setAdding(false);
      setRefresh((r) => r + 1);
    } finally {
      setBusy(false);
    }
  }
  const visible = vehicles.filter((v) =>
    `${v.registrationNo} ${v.model}`.toLowerCase().includes(term.toLowerCase()),
  );
  return (
    <section className="mobility-page">
      <div className="page-heading">
        <div>
          <div className="eyebrow muted">YOUR ELECTRIC GARAGE</div>
          <h1>{owner ? "Your vehicles" : "Assigned vehicles"}</h1>
          <p className="subtitle">
            {owner
              ? "Keep your vehicles and driver access in one place."
              : "Only active assignments shared with you appear here."}
          </p>
        </div>
        {owner && !adding && (
          <button className="primary compact" onClick={() => setAdding(true)}>
            + Add vehicle
          </button>
        )}
      </div>
      {adding && (
        <VehicleForm
          busy={busy}
          onSubmit={create}
          onCancel={() => setAdding(false)}
        />
      )}
      <div className="toolbar">
        <label htmlFor="vehicle-search">Find a vehicle</label>
        <input
          id="vehicle-search"
          type="search"
          value={term}
          onChange={(e) => setTerm(e.target.value)}
          placeholder="Registration or model"
        />
        <Link to="/stations">Find charging stations →</Link>
      </div>
      {loading ? (
        <p role="status" className="panel">
          Loading your vehicles…
        </p>
      ) : error ? (
        <div className="panel">
          <p className="error" role="alert">
            {error}
          </p>
          <button
            className="secondary"
            onClick={() => setRefresh((r) => r + 1)}
          >
            Try again
          </button>
        </div>
      ) : visible.length === 0 ? (
        <div className="panel empty">
          <h2>
            {vehicles.length
              ? "No matching vehicles"
              : owner
                ? "Your garage is ready"
                : "No active assignments"}
          </h2>
          <p>
            {vehicles.length
              ? "Try another registration or model."
              : owner
                ? "Add your first demo vehicle to get started."
                : "Ask your Car Owner to assign a vehicle using your User ID."}
          </p>
        </div>
      ) : (
        <div className="vehicle-grid">
          {visible.map((v) => (
            <article className="vehicle-card" key={v.id}>
              <div className="card-top">
                <span className="vehicle-glyph" aria-hidden="true">
                  ϟ
                </span>
                <span className="pill">SIMULATED</span>
              </div>
              <h2>
                <Link to={`/vehicles/${v.id}`}>{v.model}</Link>
              </h2>
              <p className="registration">{v.registrationNo}</p>
              <div className="battery-label">
                <span>Battery</span>
                <strong>{v.batteryPercent}%</strong>
              </div>
              <progress
                aria-label={`${v.registrationNo} simulated battery`}
                value={v.batteryPercent}
                max={100}
              />
              <div className="vehicle-metrics">
                <div>
                  <strong>
                    {v.estimatedRangeKm.toFixed(1)} <small>km</small>
                  </strong>
                  <span>Estimated range</span>
                </div>
                <div>
                  <strong>
                    {v.batteryCapacityKWh} <small>kWh</small>
                  </strong>
                  <span>{v.connectorType} connector</span>
                </div>
              </div>
              <div className="card-footer">
                <span>
                  {owner
                    ? "Owner access"
                    : v.canCharge
                      ? "Charging permitted"
                      : "View access only"}
                </span>
                <Link to={`/vehicles/${v.id}`}>View details ↗</Link>
              </div>
            </article>
          ))}
        </div>
      )}
      <p className="hint">
        Range uses capacity × battery percentage ÷ consumption. No live vehicle
        telemetry.
      </p>
    </section>
  );
}
