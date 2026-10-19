import { useState } from "react";
import type {
  AdminStation,
  AdminCharger,
  StationInput,
  ChargerInput,
} from "./api";
export function StationEditor({
  current,
  busy,
  onSave,
  onCancel,
}: {
  current?: AdminStation;
  busy: boolean;
  onSave: (body: StationInput) => void;
  onCancel: () => void;
}) {
  const [body, setBody] = useState<StationInput>(
      current
        ? {
            name: current.name,
            address: current.address,
            latitude: current.latitude,
            longitude: current.longitude,
            isActive: current.isActive,
            isDemo: current.isDemo,
          }
        : {
            name: "",
            address: "",
            latitude: 23.8,
            longitude: 90.4,
            isActive: true,
            isDemo: true,
          },
    ),
    [review, setReview] = useState(false);
  return (
    <form
      className="panel admin-editor"
      onSubmit={(e) => {
        e.preventDefault();
        setReview(true);
      }}
    >
      <h2>{current ? "Edit station" : "Create station"}</h2>
      <fieldset disabled={busy || review}>
        <label htmlFor="admin-station-name">Station name</label>
        <input
          id="admin-station-name"
          required
          minLength={2}
          maxLength={120}
          value={body.name}
          onChange={(e) => setBody({ ...body, name: e.target.value })}
        />
        <label htmlFor="admin-station-address">Address</label>
        <input
          id="admin-station-address"
          required
          minLength={3}
          maxLength={250}
          value={body.address}
          onChange={(e) => setBody({ ...body, address: e.target.value })}
        />
        <div className="admin-form-grid">
          <div>
            <label htmlFor="admin-latitude">Latitude</label>
            <input
              id="admin-latitude"
              type="number"
              required
              min={-90}
              max={90}
              step="0.000001"
              value={body.latitude}
              onChange={(e) =>
                setBody({ ...body, latitude: Number(e.target.value) })
              }
            />
          </div>
          <div>
            <label htmlFor="admin-longitude">Longitude</label>
            <input
              id="admin-longitude"
              type="number"
              required
              min={-180}
              max={180}
              step="0.000001"
              value={body.longitude}
              onChange={(e) =>
                setBody({ ...body, longitude: Number(e.target.value) })
              }
            />
          </div>
        </div>
        <label className="admin-check">
          <input
            type="checkbox"
            checked={body.isActive}
            onChange={(e) => setBody({ ...body, isActive: e.target.checked })}
          />
          Station active
        </label>
        <label className="admin-check">
          <input
            type="checkbox"
            checked={body.isDemo}
            onChange={(e) => setBody({ ...body, isDemo: e.target.checked })}
          />
          Sample / demo location
        </label>
      </fieldset>
      {review ? (
        <div
          className="admin-confirm"
          role="group"
          aria-label="Confirm station changes"
        >
          <h3>Review station changes</h3>
          <p>
            {body.name} · {body.isActive ? "Active" : "Inactive"} ·{" "}
            {body.isDemo
              ? "Sample location"
              : "Administrator supplied location"}
          </p>
          <p>
            {body.address} · {body.latitude}, {body.longitude}
          </p>
          <p>
            Changes are rejected while any charger at this station has a valid
            reservation or active session.
          </p>
          <div className="action-row">
            <button
              type="button"
              className="primary compact"
              disabled={busy}
              onClick={() => onSave(body)}
            >
              {busy ? "Saving…" : "Confirm station save"}
            </button>
            <button
              type="button"
              className="secondary"
              disabled={busy}
              onClick={() => setReview(false)}
            >
              Back to editing
            </button>
          </div>
        </div>
      ) : (
        <button className="primary compact" disabled={busy}>
          Review station changes
        </button>
      )}
      <button
        type="button"
        className="text-button"
        disabled={busy}
        onClick={onCancel}
      >
        Cancel editing
      </button>
    </form>
  );
}
export function ChargerEditor({
  station,
  current,
  busy,
  onSave,
  onCancel,
}: {
  station: AdminStation;
  current?: AdminCharger;
  busy: boolean;
  onSave: (body: ChargerInput) => void;
  onCancel: () => void;
}) {
  const [body, setBody] = useState<ChargerInput>(
      current
        ? {
            chargerCode: current.chargerCode,
            connectorType: current.connectorType,
            powerKW: current.powerKW,
            pricePerKWh: current.pricePerKWh,
            isActive: current.isActive,
          }
        : {
            chargerCode: "",
            connectorType: "CCS2",
            powerKW: 60,
            pricePerKWh: 25,
            isActive: station.isActive,
          },
    ),
    [review, setReview] = useState(false);
  return (
    <form
      className="panel admin-editor"
      onSubmit={(e) => {
        e.preventDefault();
        setReview(true);
      }}
    >
      <h2>
        {current ? "Edit charger" : "Create charger"} · {station.name}
      </h2>
      <fieldset disabled={busy || review}>
        <label htmlFor="admin-charger-code">Charger code</label>
        <input
          id="admin-charger-code"
          required
          minLength={2}
          maxLength={30}
          value={body.chargerCode}
          onChange={(e) => setBody({ ...body, chargerCode: e.target.value })}
        />
        <label htmlFor="admin-connector">Connector type</label>
        <select
          id="admin-connector"
          value={body.connectorType}
          onChange={(e) => setBody({ ...body, connectorType: e.target.value })}
        >
          {["CCS2", "Type2", "CHAdeMO"].map((c) => (
            <option key={c}>{c}</option>
          ))}
        </select>
        <div className="admin-form-grid">
          <div>
            <label htmlFor="admin-power">Power (kW)</label>
            <input
              id="admin-power"
              type="number"
              min="0.01"
              max="999999.99"
              step="0.01"
              required
              value={body.powerKW}
              onChange={(e) =>
                setBody({ ...body, powerKW: Number(e.target.value) })
              }
            />
          </div>
          <div>
            <label htmlFor="admin-tariff">Tariff (BDT/kWh)</label>
            <input
              id="admin-tariff"
              type="number"
              min="0.01"
              max="99999999.99"
              step="0.01"
              required
              value={body.pricePerKWh}
              onChange={(e) =>
                setBody({ ...body, pricePerKWh: Number(e.target.value) })
              }
            />
          </div>
        </div>
        <label className="admin-check">
          <input
            type="checkbox"
            checked={body.isActive}
            onChange={(e) => setBody({ ...body, isActive: e.target.checked })}
          />
          Charger enabled
        </label>
      </fieldset>
      {review ? (
        <div
          className="admin-confirm"
          role="group"
          aria-label="Confirm charger changes"
        >
          <h3>Review charger changes</h3>
          <p>
            {body.chargerCode} · {body.connectorType} · {body.powerKW} kW ·{" "}
            {body.pricePerKWh.toFixed(2)} BDT/kWh ·{" "}
            {body.isActive ? "Enabled" : "Offline"}
          </p>
          <p>
            A charger with a valid reservation or active session cannot be
            edited or disabled.
          </p>
          <div className="action-row">
            <button
              type="button"
              className="primary compact"
              disabled={busy}
              onClick={() => onSave(body)}
            >
              {busy ? "Saving…" : "Confirm charger save"}
            </button>
            <button
              type="button"
              className="secondary"
              disabled={busy}
              onClick={() => setReview(false)}
            >
              Back to editing
            </button>
          </div>
        </div>
      ) : (
        <button className="primary compact" disabled={busy}>
          Review charger changes
        </button>
      )}
      <button
        type="button"
        className="text-button"
        disabled={busy}
        onClick={onCancel}
      >
        Cancel editing
      </button>
    </form>
  );
}
