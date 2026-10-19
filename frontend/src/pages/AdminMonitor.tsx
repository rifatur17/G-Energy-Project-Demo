import { useEffect, useState } from "react";
import { AdminFrame } from "../features/admin/AdminFrame";
import {
  useAdminApi,
  type AdminRow,
  type MonitorKind,
} from "../features/admin/api";
const config: Record<
  MonitorKind,
  { title: string; columns: [string, string][]; statuses?: string[] }
> = {
  bookings: {
    title: "Reservation monitor",
    statuses: ["Reserved", "Expired", "Cancelled", "Consumed"],
    columns: [
      ["bookingCode", "Booking"],
      ["stationName", "Station"],
      ["stationId", "Station ID"],
      ["chargerCode", "Charger"],
      ["vehicleRegistration", "Vehicle"],
      ["bookedByUserCode", "Booked by"],
      ["persistedStatus", "Persisted state"],
      ["createdAtUtc", "Created"],
      ["expiresAtUtc", "Expires"],
      ["sessionId", "Related session"],
    ],
  },
  "charging-sessions": {
    title: "Charging session monitor",
    statuses: ["Active", "Completed"],
    columns: [
      ["bookingCode", "Booking"],
      ["stationName", "Station"],
      ["stationId", "Station ID"],
      ["chargerCode", "Charger"],
      ["vehicleRegistration", "Vehicle"],
      ["startedByUserCode", "Started by"],
      ["payerUserCode", "Payer"],
      ["startedAtUtc", "Started"],
      ["endedAtUtc", "Finished"],
      ["initialBatteryPercent", "Initial battery"],
      ["batteryPercent", "Battery"],
      ["energyKWh", "Energy"],
      ["cost", "Server-calculated cost"],
      ["paymentStatus", "Payment"],
      ["paymentReference", "Payment reference"],
      ["settlementNote", "Settlement note"],
    ],
  },
  "wallet-transactions": {
    title: "Simulated transaction monitor",
    statuses: ["Completed", "Failed"],
    columns: [
      ["reference", "Reference"],
      ["ownerUserCode", "Wallet Owner"],
      ["type", "Type"],
      ["amount", "Amount"],
      ["description", "Description"],
      ["createdAtUtc", "Created"],
      ["chargingSessionId", "Related session"],
      ["originalReference", "Original payment"],
      ["refundReference", "Refund"],
      ["ownerCanRefund", "Owner refund eligible"],
    ],
  },
  users: {
    title: "User access overview",
    columns: [
      ["userCode", "User code"],
      ["role", "Role"],
      ["isActive", "Active account"],
      ["createdAtUtc", "Registered"],
    ],
  },
  wallets: {
    title: "Demo wallet overview",
    columns: [
      ["ownerUserCode", "Owner"],
      ["balance", "Current balance"],
      ["updatedAtUtc", "Updated"],
    ],
  },
};
function display(key: string, value: AdminRow[string]) {
  if (value === null) return "—";
  if (typeof value === "boolean") return value ? "Yes" : "No";
  if (key.endsWith("AtUtc")) return new Date(String(value)).toLocaleString();
  if (["amount", "cost", "balance"].includes(key))
    return Number(value).toFixed(2) + " BDT";
  if (key.toLowerCase().includes("batterypercent"))
    return Number(value).toFixed(2) + "%";
  if (key === "energyKWh") return Number(value).toFixed(3) + " kWh";
  return String(value);
}
export function AdminMonitor({ kind }: { kind: MonitorKind }) {
  const api = useAdminApi(),
    details = config[kind];
  const [rows, setRows] = useState<AdminRow[]>([]),
    [loading, setLoading] = useState(true),
    [error, setError] = useState(""),
    [filters, setFilters] = useState<Record<string, string>>({}),
    [query, setQuery] = useState<Record<string, string>>({}),
    [refresh, setRefresh] = useState(0),
    [more, setMore] = useState(false),
    [busy, setBusy] = useState(false);
  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    api
      .monitor(kind, query)
      .then((data) => {
        if (active) {
          setRows(data);
          setMore(data.length === 20);
        }
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
  }, [api, kind, query, refresh]);
  function apply(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    const q: Record<string, string> = {};
    for (const [key, value] of Object.entries(filters)) {
      if (!value) continue;
      q[key] = ["from", "to"].includes(key)
        ? new Date(value).toISOString()
        : value;
    }
    if (q.from && q.to && q.from > q.to) {
      setError("Start time must not follow end time.");
      return;
    }
    setQuery(q);
  }
  async function older() {
    if (busy || !rows.length) return;
    setBusy(true);
    try {
      const data = await api.monitor(kind, {
        ...query,
        before: String(rows[rows.length - 1].id),
      });
      setRows((old) => [...old, ...data]);
      setMore(data.length === 20);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  const timeFilters = !["users", "wallets"].includes(kind);
  return (
    <AdminFrame
      title={details.title}
      subtitle="Read-only operational records. All displayed payments and balances are simulated."
    >
      <form className="panel admin-filters" onSubmit={apply}>
        {details.statuses && (
          <div>
            <label htmlFor="monitor-status">Status</label>
            <select
              id="monitor-status"
              value={filters.status ?? ""}
              onChange={(e) =>
                setFilters({ ...filters, status: e.target.value })
              }
            >
              <option value="">All statuses</option>
              {details.statuses.map((s) => (
                <option key={s}>{s}</option>
              ))}
            </select>
          </div>
        )}
        {kind === "users" && (
          <div>
            <label htmlFor="monitor-role">Role</label>
            <select
              id="monitor-role"
              value={filters.role ?? ""}
              onChange={(e) => setFilters({ ...filters, role: e.target.value })}
            >
              <option value="">All roles</option>
              {["Admin", "CarOwner", "CarDriver"].map((v) => (
                <option key={v}>{v}</option>
              ))}
            </select>
          </div>
        )}
        {kind === "wallet-transactions" && (
          <div>
            <label htmlFor="monitor-type">Transaction type</label>
            <select
              id="monitor-type"
              value={filters.type ?? ""}
              onChange={(e) => setFilters({ ...filters, type: e.target.value })}
            >
              <option value="">All types</option>
              {["TopUp", "ChargingPayment", "Refund"].map((v) => (
                <option key={v}>{v}</option>
              ))}
            </select>
          </div>
        )}
        {kind === "charging-sessions" && (
          <div>
            <label htmlFor="monitor-payment">Payment status</label>
            <select
              id="monitor-payment"
              value={filters.payment ?? ""}
              onChange={(e) =>
                setFilters({ ...filters, payment: e.target.value })
              }
            >
              <option value="">All payments</option>
              <option>Paid</option>
              <option>Unpaid</option>
            </select>
          </div>
        )}
        {["bookings", "charging-sessions"].includes(kind) && (
          <div>
            <label htmlFor="monitor-station">Station ID</label>
            <input
              id="monitor-station"
              type="number"
              min="1"
              value={filters.stationId ?? ""}
              onChange={(e) =>
                setFilters({ ...filters, stationId: e.target.value })
              }
            />
          </div>
        )}
        {timeFilters && (
          <>
            {[
              ["from", "From (local time)"],
              ["to", "To (local time)"],
            ].map(([key, label]) => (
              <div key={key}>
                <label htmlFor={`monitor-${key}`}>{label}</label>
                <input
                  id={`monitor-${key}`}
                  type="datetime-local"
                  value={filters[key] ?? ""}
                  onChange={(e) =>
                    setFilters({ ...filters, [key]: e.target.value })
                  }
                />
              </div>
            ))}
          </>
        )}
        <button className="primary compact" disabled={busy}>
          Apply filters
        </button>
        <button
          type="button"
          className="secondary"
          disabled={busy}
          onClick={() => setRefresh((v) => v + 1)}
        >
          Refresh records
        </button>
      </form>
      {error && (
        <p className="panel error" role="alert">
          {error}
        </p>
      )}
      {loading ? (
        <p role="status">Loading records…</p>
      ) : rows.length === 0 ? (
        <p className="panel empty">No matching records.</p>
      ) : (
        <div className="admin-monitor-list">
          {rows.map((row) => (
            <article className="panel monitor-card" key={row.id}>
              <div className="card-top">
                <h2>Record #{row.id}</h2>
                {row.status && (
                  <span className="pill">{String(row.status)}</span>
                )}
              </div>
              <dl>
                {details.columns.map(([key, label]) => (
                  <div key={key}>
                    <dt>{label}</dt>
                    <dd>{display(key, row[key] ?? null)}</dd>
                  </div>
                ))}
              </dl>
            </article>
          ))}
        </div>
      )}
      {more && (
        <button
          className="secondary"
          disabled={busy}
          onClick={() => void older()}
        >
          Load older records
        </button>
      )}
      <p className="hint">
        Newest records first. Times are shown in your browser's local timezone.
        Charging values are the last persisted server snapshot. Refund actions
        remain with the Owner's existing wallet flow; this view cannot change a
        balance or payment state.
      </p>
    </AdminFrame>
  );
}
