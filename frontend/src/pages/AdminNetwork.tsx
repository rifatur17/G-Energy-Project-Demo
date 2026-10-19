import { useEffect, useRef, useState } from "react";
import { AdminFrame } from "../features/admin/AdminFrame";
import { StationEditor, ChargerEditor } from "../features/admin/NetworkForms";
import {
  useAdminApi,
  type AdminStation,
  type AdminCharger,
  type StationInput,
  type ChargerInput,
} from "../features/admin/api";
export function AdminNetwork() {
  const api = useAdminApi();
  const [stations, setStations] = useState<AdminStation[]>([]),
    [selected, setSelected] = useState<AdminStation | null>(null),
    [chargers, setChargers] = useState<AdminCharger[]>([]),
    [loading, setLoading] = useState(true),
    [loadingChargers, setLoadingChargers] = useState(false),
    [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [message, setMessage] = useState(""),
    [search, setSearch] = useState(""),
    [active, setActive] = useState(""),
    [query, setQuery] = useState({ search: "", active: "" }),
    [refresh, setRefresh] = useState(0),
    [more, setMore] = useState(false),
    [moreChargers, setMoreChargers] = useState(false),
    [stationEdit, setStationEdit] = useState<AdminStation | "new" | null>(null),
    [chargerEdit, setChargerEdit] = useState<AdminCharger | "new" | null>(null);
  const submitting = useRef(false);
  useEffect(() => {
    let alive = true;
    setLoading(true);
    api
      .stations(query.search, query.active)
      .then((rows) => {
        if (alive) {
          setStations(rows);
          setMore(rows.length === 20);
        }
      })
      .catch((e) => {
        if (alive) setError(e.message);
      })
      .finally(() => {
        if (alive) setLoading(false);
      });
    return () => {
      alive = false;
    };
  }, [api, query, refresh]);
  useEffect(() => {
    let alive = true;
    setChargers([]);
    setChargerEdit(null);
    if (selected) {
      setLoadingChargers(true);
      api
        .chargers(selected.id)
        .then((rows) => {
          if (alive) {
            setChargers(rows);
            setMoreChargers(rows.length === 20);
          }
        })
        .catch((e) => {
          if (alive) setError(e.message);
        })
        .finally(() => {
          if (alive) setLoadingChargers(false);
        });
    }
    return () => {
      alive = false;
    };
  }, [api, selected, refresh]);
  async function saveStation(body: StationInput) {
    if (submitting.current) return;
    submitting.current = true;
    setBusy(true);
    setError("");
    setMessage("");
    try {
      const saved = await api.saveStation(
        body,
        stationEdit !== "new" && stationEdit ? stationEdit : undefined,
      );
      setStationEdit(null);
      setSelected(saved);
      setRefresh((v) => v + 1);
      setMessage(`Station saved: ${saved.name}.`);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  }
  async function saveCharger(body: ChargerInput) {
    if (!selected || submitting.current) return;
    submitting.current = true;
    setBusy(true);
    setError("");
    setMessage("");
    try {
      const saved = await api.saveCharger(
        selected.id,
        body,
        chargerEdit !== "new" && chargerEdit ? chargerEdit : undefined,
      );
      setChargerEdit(null);
      setRefresh((v) => v + 1);
      setMessage(`Charger saved: ${saved.chargerCode}.`);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  }
  async function older(charger: boolean) {
    if (submitting.current) return;
    submitting.current = true;
    setBusy(true);
    try {
      if (charger && selected) {
        const rows = await api.chargers(
          selected.id,
          chargers[chargers.length - 1]?.id,
        );
        setChargers((old) => [...old, ...rows]);
        setMoreChargers(rows.length === 20);
      } else {
        const rows = await api.stations(
          query.search,
          query.active,
          stations[stations.length - 1]?.id,
        );
        setStations((old) => [...old, ...rows]);
        setMore(rows.length === 20);
      }
    } catch (e) {
      setError((e as Error).message);
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  }
  return (
    <AdminFrame
      title="Care for the charging network."
      subtitle="Manage the shared station network with safe, reviewed changes."
    >
      {error && (
        <p className="panel error" role="alert">
          {error}
        </p>
      )}
      {message && (
        <p className="panel success" role="status">
          {message}
        </p>
      )}
      <form
        className="panel admin-filters"
        onSubmit={(e) => {
          e.preventDefault();
          setError("");
          setSelected(null);
          setStationEdit(null);
          setQuery({ search, active });
        }}
      >
        <div>
          <label htmlFor="admin-search">Search stations</label>
          <input
            id="admin-search"
            maxLength={120}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <div>
          <label htmlFor="admin-active">Station status</label>
          <select
            id="admin-active"
            value={active}
            onChange={(e) => setActive(e.target.value)}
          >
            <option value="">All stations</option>
            <option value="true">Active</option>
            <option value="false">Inactive</option>
          </select>
        </div>
        <button className="primary compact" disabled={busy}>
          Search
        </button>
        <button
          type="button"
          className="secondary"
          disabled={busy}
          onClick={() => {
            setError("");
            setRefresh((v) => v + 1);
          }}
        >
          Refresh network
        </button>
        <button
          type="button"
          className="secondary"
          disabled={busy}
          onClick={() => {
            setStationEdit("new");
            setChargerEdit(null);
          }}
        >
          New station
        </button>
      </form>
      {stationEdit && (
        <StationEditor
          key={
            stationEdit === "new"
              ? "new"
              : `${stationEdit.id}:${stationEdit.version}`
          }
          current={stationEdit === "new" ? undefined : stationEdit}
          busy={busy}
          onSave={(body) => void saveStation(body)}
          onCancel={() => setStationEdit(null)}
        />
      )}
      <div className="admin-network-grid">
        <section>
          <h2>Stations</h2>
          {loading ? (
            <p role="status">Loading stations…</p>
          ) : stations.length === 0 ? (
            <div className="panel empty">No matching stations.</div>
          ) : (
            stations.map((s) => (
              <article
                className={`panel admin-station ${selected?.id === s.id ? "selected" : ""}`}
                key={s.id}
              >
                <div className="card-top">
                  <span className="pill">
                    {s.isActive ? "Active" : "Inactive"}
                  </span>
                  <span className="hint">
                    Station #{s.id} ·{" "}
                    {s.isDemo ? "Sample location" : "Administrator supplied"}
                  </span>
                </div>
                <h3>{s.name}</h3>
                <p>{s.address}</p>
                <p className="hint">
                  {s.latitude}, {s.longitude} · {s.chargerCount} chargers
                </p>
                <div className="action-row">
                  <button
                    className="secondary"
                    disabled={busy}
                    onClick={() => {
                      setSelected(s);
                      setStationEdit(null);
                    }}
                  >
                    Manage chargers · #{s.id}
                  </button>
                  <button
                    className="secondary"
                    disabled={busy}
                    onClick={() => {
                      setStationEdit(s);
                      setChargerEdit(null);
                    }}
                  >
                    Edit station · #{s.id}
                  </button>
                </div>
              </article>
            ))
          )}
          {more && (
            <button
              className="secondary"
              disabled={busy}
              onClick={() => void older(false)}
            >
              Load older stations
            </button>
          )}
        </section>
        <section>
          <h2>
            {selected ? `Chargers · ${selected.name}` : "Station chargers"}
          </h2>
          {!selected ? (
            <p className="panel empty">
              Select a station to view or manage its chargers.
            </p>
          ) : (
            <>
              <button
                className="secondary"
                disabled={busy}
                onClick={() => setChargerEdit("new")}
              >
                New charger
              </button>
              {chargerEdit && (
                <ChargerEditor
                  key={
                    chargerEdit === "new"
                      ? "new"
                      : `${chargerEdit.id}:${chargerEdit.rowVersion}`
                  }
                  station={selected}
                  current={chargerEdit === "new" ? undefined : chargerEdit}
                  busy={busy}
                  onSave={(body) => void saveCharger(body)}
                  onCancel={() => setChargerEdit(null)}
                />
              )}{" "}
              {loadingChargers ? (
                <p role="status">Loading chargers…</p>
              ) : chargers.length === 0 ? (
                <p className="panel empty">No chargers at this station.</p>
              ) : (
                chargers.map((c) => (
                  <article className="panel admin-charger" key={c.id}>
                    <div className="card-top">
                      <strong>{c.chargerCode}</strong>
                      <span className="pill">{c.effectiveStatus}</span>
                    </div>
                    <p>
                      {c.connectorType} · {c.powerKW} kW ·{" "}
                      {c.pricePerKWh.toFixed(2)} BDT/kWh
                    </p>
                    <p className="hint">Configured: {c.configuredStatus}</p>
                    <button
                      className="secondary"
                      disabled={busy}
                      onClick={() => {
                        setChargerEdit(c);
                        setStationEdit(null);
                      }}
                    >
                      Edit charger · #{c.id}
                    </button>
                  </article>
                ))
              )}
              {moreChargers && (
                <button
                  className="secondary"
                  disabled={busy}
                  onClick={() => void older(true)}
                >
                  Load older chargers
                </button>
              )}
            </>
          )}
        </section>
      </div>
      <p className="hint">
        Sample locations are not verified real charging stations. Deactivation
        preserves all booking, session and payment history.
      </p>
    </AdminFrame>
  );
}
