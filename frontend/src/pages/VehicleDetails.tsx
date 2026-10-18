import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useAuth } from "../features/auth/AuthContext";
import {
  useMobilityApi,
  type Vehicle,
  type Driver,
  type VehicleInput,
} from "../features/mobility/api";
import { VehicleForm } from "../features/mobility/VehicleForm";
export function VehicleDetails() {
  const id = Number(useParams().id);
  const { user } = useAuth();
  const owner = user?.role === "CarOwner";
  const api = useMobilityApi();
  const [vehicle, setVehicle] = useState<Vehicle | null>(null);
  const [drivers, setDrivers] = useState<Driver[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [editing, setEditing] = useState(false);
  const [busy, setBusy] = useState(false);
  const [code, setCode] = useState("");
  const [found, setFound] = useState<Driver | null>(null);
  const [canCharge, setCanCharge] = useState(false);
  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    Promise.all([
      api.vehicle(id),
      owner ? api.drivers(id) : Promise.resolve([]),
    ])
      .then(([v, d]) => {
        if (active) {
          setVehicle(v);
          setDrivers(d);
        }
      })
      .catch((e) => {
        if (active) {
          setVehicle(null);
          setError(e.message);
        }
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [api, id, owner, refresh]);
  async function perform(action: () => Promise<unknown>, message: string) {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await action();
      setNotice(message);
      setRefresh((r) => r + 1);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  async function update(input: VehicleInput) {
    if (!vehicle) return;
    setBusy(true);
    try {
      await api.update(vehicle, input);
      setEditing(false);
      setNotice("Vehicle updated.");
      setRefresh((r) => r + 1);
    } finally {
      setBusy(false);
    }
  }
  async function lookup() {
    setBusy(true);
    setFound(null);
    setError("");
    try {
      setFound(await api.lookup(code));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  if (loading)
    return (
      <section className="mobility-page panel" role="status">
        Loading vehicle details…
      </section>
    );
  if (!vehicle)
    return (
      <section className="mobility-page panel">
        <h1>Vehicle unavailable</h1>
        <p className="error" role="alert">
          {error || "This vehicle is not available to your account."}
        </p>
        <Link to="/vehicles">Back to vehicles</Link>
        <button className="secondary" onClick={() => setRefresh((r) => r + 1)}>
          Try again
        </button>
      </section>
    );
  return (
    <section className="mobility-page">
      <Link className="back-link" to="/vehicles">
        ← Your vehicles
      </Link>
      <div className="page-heading">
        <div>
          <div className="eyebrow muted">{vehicle.registrationNo}</div>
          <h1>{vehicle.model}</h1>
          <p className="subtitle">
            Simulated vehicle data · {vehicle.connectorType} connector
          </p>
        </div>
        {owner && !editing && (
          <button className="secondary" onClick={() => setEditing(true)}>
            Edit vehicle
          </button>
        )}
      </div>
      {error && (
        <div className="error" role="alert">
          {error}{" "}
          <button onClick={() => setRefresh((r) => r + 1)}>
            Reload details
          </button>
        </div>
      )}
      {notice && (
        <p className="success-note" role="status">
          {notice}
        </p>
      )}
      {editing && (
        <VehicleForm
          initial={vehicle}
          busy={busy}
          onSubmit={update}
          onCancel={() => setEditing(false)}
        />
      )}
      <div className="vehicle-detail-grid">
        <section className="panel">
          <span className="pill">SIMULATED STATUS</span>
          <div className="range-number">
            {vehicle.batteryPercent}
            <small>%</small>
          </div>
          <progress
            aria-label="Simulated battery"
            value={vehicle.batteryPercent}
            max={100}
          />
          <div className="vehicle-metrics">
            <div>
              <strong>
                {vehicle.estimatedRangeKm.toFixed(2)} <small>km</small>
              </strong>
              <span>Estimated range</span>
            </div>
            <div>
              <strong>
                {vehicle.batteryCapacityKWh} <small>kWh</small>
              </strong>
              <span>Battery capacity</span>
            </div>
          </div>
          <p className="hint">
            Consumption: {vehicle.consumptionKWhPerKm} kWh/km
          </p>
          <p className="hint">
            Updated {new Date(vehicle.updatedAtUtc).toLocaleString()}
          </p>
          <Link
            className="primary"
            to={`/stations?connector=${encodeURIComponent(vehicle.connectorType)}`}
          >
            Find compatible stations →
          </Link>
        </section>
        <section className="panel">
          <h2>{owner ? "Driver access" : "Your access"}</h2>
          {!owner ? (
            <>
              <p>
                {vehicle.canCharge
                  ? "Charging is permitted by your Owner."
                  : "View access only. Your Owner has not enabled charging permission."}
              </p>
              <p className="hint">
                Booking and charging actions will connect in Member 3’s module.
              </p>
            </>
          ) : (
            <>
              <p className="subtitle">
                Find a registered driver by User ID. You stay in control of
                charging permission.
              </p>
              <div className="driver-lookup">
                <label htmlFor="driver-code">Driver User ID</label>
                <div className="inline-input">
                  <input
                    id="driver-code"
                    value={code}
                    onChange={(e) => {
                      setCode(e.target.value);
                      setFound(null);
                    }}
                    maxLength={24}
                    placeholder="GE-DRV-001002"
                  />
                  <button
                    className="secondary"
                    disabled={busy || !code.trim()}
                    onClick={lookup}
                  >
                    Find driver
                  </button>
                </div>
              </div>
              {found && (
                <div className="assignment-preview">
                  <strong>{found.fullName}</strong>
                  <code>{found.userCode}</code>
                  <label className="checkbox-label">
                    <input
                      type="checkbox"
                      checked={canCharge}
                      onChange={(e) => setCanCharge(e.target.checked)}
                    />
                    Allow charging with this vehicle
                  </label>
                  <button
                    className="primary compact"
                    disabled={busy}
                    onClick={() =>
                      perform(async () => {
                        await api.assign(vehicle, found.userCode, canCharge);
                        setFound(null);
                        setCode("");
                        setCanCharge(false);
                      }, "Driver assigned.")
                    }
                  >
                    Assign driver
                  </button>
                </div>
              )}
              <h3 className="small-heading">Active assignments</h3>
              {drivers.length === 0 ? (
                <p className="hint">No drivers assigned yet.</p>
              ) : (
                drivers.map((d) => (
                  <article className="assignment-row" key={d.driverUserId}>
                    <div>
                      <strong>{d.fullName}</strong>
                      <code>{d.userCode}</code>
                      <span className="hint">
                        {d.canCharge
                          ? "Charging permitted"
                          : "View access only"}
                      </span>
                    </div>
                    <div className="assignment-actions">
                      <button
                        className="secondary"
                        disabled={busy}
                        onClick={() =>
                          perform(
                            () => api.permission(vehicle, d, !d.canCharge),
                            "Driver permission updated.",
                          )
                        }
                      >
                        {d.canCharge ? "Disable charging" : "Enable charging"}
                      </button>
                      <button
                        className="danger-link"
                        disabled={busy}
                        onClick={() =>
                          perform(
                            () => api.revoke(vehicle, d),
                            "Assignment revoked.",
                          )
                        }
                      >
                        Revoke
                      </button>
                    </div>
                  </article>
                ))
              )}
            </>
          )}
        </section>
      </div>
    </section>
  );
}
