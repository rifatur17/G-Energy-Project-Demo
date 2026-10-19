import { useEffect, useState } from "react";
import { useAuth } from "../features/auth/AuthContext";
import { AdminFrame } from "../features/admin/AdminFrame";
import { useAdminApi, type AdminSummary } from "../features/admin/api";
export function AdminDashboard() {
  const api = useAdminApi(),
    { user } = useAuth();
  const [summary, setSummary] = useState<AdminSummary | null>(null),
    [error, setError] = useState(""),
    [loading, setLoading] = useState(true),
    [refresh, setRefresh] = useState(0);
  useEffect(() => {
    let active = true,
      pending = false;
    async function load() {
      if (pending) return;
      pending = true;
      try {
        const s = await api.dashboard();
        if (active) {
          setSummary(s);
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
    }, 15000);
    return () => {
      active = false;
      clearInterval(timer);
    };
  }, [api, refresh]);
  const counts: [keyof AdminSummary, string][] = [
    ["totalUsers", "Registered users"],
    ["activeStations", "Active stations"],
    ["totalChargers", "Total chargers"],
    ["availableChargers", "Available chargers"],
    ["activeBookings", "Valid reservations"],
    ["activeSessions", "Active charging sessions"],
    ["completedSessions", "Completed sessions"],
    ["completedChargingPayments", "Completed charging payments"],
    ["successfulTopUps", "Successful demo top-ups"],
    ["failedTopUps", "Failed demo top-ups"],
    ["refundCount", "Completed demo refunds"],
  ];
  return (
    <AdminFrame
      title="Network at a glance."
      subtitle="Live operational counts and a clear view of simulated payments."
    >
      <div className="page-heading">
        <div>
          <h2>Your workspace</h2>
          <p className="hint">
            Signed in as {user?.userCode}. Operations and ledger monitoring are
            read-only.
          </p>
        </div>
        <button className="secondary" onClick={() => setRefresh((v) => v + 1)}>
          Refresh overview
        </button>
      </div>
      {error && (
        <p className="panel error" role="alert">
          {error}
        </p>
      )}
      {loading ? (
        <p role="status">Loading network overview…</p>
      ) : (
        summary && (
          <>
            <div className="admin-stats">
              {counts.map(([key, label]) => (
                <article className="panel admin-stat" key={key}>
                  <span>{label}</span>
                  <strong>{summary[key]}</strong>
                </article>
              ))}
            </div>
            <section className="panel admin-money">
              <h2>Simulated payment activity</h2>
              <p>
                No real funds were collected. Failed top-ups are excluded from
                completed totals.
              </p>
              <dl>
                {(
                  [
                    ["grossChargingPayments", "Gross charging payments"],
                    ["refunds", "Refunds"],
                    ["netChargingPayments", "Net charging payments"],
                    ["totalWalletBalance", "Current demo wallet balances"],
                  ] as [keyof AdminSummary, string][]
                ).map(([key, label]) => (
                  <div key={key}>
                    <dt>{label}</dt>
                    <dd>
                      {Number(summary[key]).toFixed(2)} {summary.currency}
                    </dd>
                  </div>
                ))}
              </dl>
            </section>
            <p className="hint">
              Server snapshot {new Date(summary.serverTimeUtc).toLocaleString()}
              . Reservations exclude expired holds; availability excludes
              inactive stations, Offline chargers and occupied charging points.
              Updates every 15 seconds while visible.
            </p>
          </>
        )
      )}
    </AdminFrame>
  );
}
