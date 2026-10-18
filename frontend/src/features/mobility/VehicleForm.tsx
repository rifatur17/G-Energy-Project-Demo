import { useState, type FormEvent } from "react";
import { connectors, type VehicleInput } from "./api";
export function VehicleForm({
  initial,
  busy,
  onSubmit,
  onCancel,
}: {
  initial?: VehicleInput;
  busy: boolean;
  onSubmit: (v: VehicleInput) => Promise<void>;
  onCancel: () => void;
}) {
  const [error, setError] = useState("");
  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    setError("");
    try {
      await onSubmit({
        registrationNo: String(f.get("registrationNo")).trim(),
        model: String(f.get("model")).trim(),
        connectorType: String(f.get("connectorType")),
        batteryCapacityKWh: Number(f.get("capacity")),
        batteryPercent: Number(f.get("battery")),
        consumptionKWhPerKm: Number(f.get("consumption")),
      });
    } catch (e) {
      setError((e as Error).message);
    }
  }
  return (
    <form className="vehicle-form" onSubmit={submit}>
      <h2>{initial ? "Edit vehicle" : "Add a vehicle"}</h2>
      <p className="subtitle">
        Demo vehicle data. Battery and range are simulated.
      </p>
      <fieldset disabled={busy}>
        <div className="form-row">
          <div>
            <label htmlFor="registration">Registration number</label>
            <input
              id="registration"
              name="registrationNo"
              defaultValue={initial?.registrationNo}
              minLength={2}
              maxLength={32}
              required
            />
          </div>
          <div>
            <label htmlFor="model">Vehicle model</label>
            <input
              id="model"
              name="model"
              defaultValue={initial?.model}
              minLength={2}
              maxLength={100}
              required
            />
          </div>
        </div>
        <div className="field-grid">
          <div>
            <label htmlFor="connector">Connector</label>
            <select
              id="connector"
              name="connectorType"
              defaultValue={initial?.connectorType ?? "CCS2"}
            >
              {connectors.map((c) => (
                <option key={c}>{c}</option>
              ))}
            </select>
          </div>
          <div>
            <label htmlFor="capacity">Battery capacity (kWh)</label>
            <input
              id="capacity"
              name="capacity"
              type="number"
              min="0.01"
              max="999999.99"
              step="0.01"
              defaultValue={initial?.batteryCapacityKWh ?? 50}
              required
            />
          </div>
          <div>
            <label htmlFor="battery">Simulated battery (%)</label>
            <input
              id="battery"
              name="battery"
              type="number"
              min="0"
              max="100"
              step="0.01"
              defaultValue={initial?.batteryPercent ?? 40}
              required
            />
          </div>
          <div>
            <label htmlFor="consumption">Consumption (kWh/km)</label>
            <input
              id="consumption"
              name="consumption"
              type="number"
              min="0.0001"
              max="9999.9999"
              step="0.0001"
              defaultValue={initial?.consumptionKWhPerKm ?? 0.18}
              required
            />
          </div>
        </div>
      </fieldset>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <div className="actions">
        <button className="primary compact" disabled={busy}>
          {busy ? "Saving…" : initial ? "Save changes" : "Add vehicle"}
        </button>
        <button
          className="secondary"
          type="button"
          onClick={onCancel}
          disabled={busy}
        >
          Cancel
        </button>
      </div>
    </form>
  );
}
