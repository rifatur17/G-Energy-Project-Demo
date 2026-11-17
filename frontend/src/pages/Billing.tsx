import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import {
  methodLabel,
  useBillingApi,
  type BillingLine,
  type BillingSummary,
  type Statement,
  type StatementDetail,
} from "../features/billing/api";
const taka = (n: number) => `৳${n.toFixed(2)}`;
const date = (iso: string) =>
  new Date(iso).toLocaleDateString(undefined, {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
const results: Record<string, { kind: "success" | "error"; text: string }> = {
  paid: {
    kind: "success",
    text: "Payment confirmed by SSLCommerz. Your statement is paid.",
  },
  failed: {
    kind: "error",
    text: "The payment failed at SSLCommerz. Nothing was charged; you can try again.",
  },
  cancelled: {
    kind: "error",
    text: "You cancelled the SSLCommerz checkout. The statement is still due.",
  },
  unconfirmed: {
    kind: "error",
    text: "SSLCommerz did not confirm this payment, so the statement was not marked paid.",
  },
};
export function Billing() {
  const api = useBillingApi();
  const [params, setParams] = useSearchParams();
  const [summary, setSummary] = useState<BillingSummary | null>(null),
    [unbilled, setUnbilled] = useState<BillingLine[]>([]),
    [statements, setStatements] = useState<Statement[]>([]),
    [open, setOpen] = useState<StatementDetail | null>(null),
    [loading, setLoading] = useState(true),
    [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [message, setMessage] = useState("");
  const submitting = useRef(false);
  const load = useCallback(async () => {
    const [s, u, st] = await Promise.all([
      api.summary(),
      api.unbilled(),
      api.statements(),
    ]);
    setSummary(s);
    setUnbilled(u);
    setStatements(st);
  }, [api]);
  const show = useCallback(
    async (id: number) => {
      setOpen(await api.statement(id));
    },
    [api],
  );
  useEffect(() => {
    let active = true;
    load()
      .catch((e) => active && setError(e.message))
      .finally(() => active && setLoading(false));
    return () => {
      active = false;
    };
  }, [load]);
  useEffect(() => {
    const result = params.get("payment");
    if (!result) return;
    const known = results[result];
    if (known?.kind === "success") setMessage(known.text);
    else setError(known?.text ?? "Payment result is unknown.");
    const id = Number(params.get("statement"));
    if (id > 0) void show(id).catch(() => {});
    setParams({}, { replace: true });
  }, [params, setParams, show]);
  async function run(action: () => Promise<void>) {
    if (submitting.current) return;
    submitting.current = true;
    setBusy(true);
    setError("");
    setMessage("");
    try {
      await action();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  }
  const closeMonth = () =>
    run(async () => {
      const s = await api.closeMonth();
      setMessage(
        `Statement ${s.statementNumber} issued for ${taka(s.total)}. Due by ${date(s.dueAtUtc)}.`,
      );
      await load();
      await show(s.id);
    });
  const pay = (s: Statement) =>
    run(async () => {
      const checkout = await api.checkout(s.id);
      setMessage("Opening SSLCommerz secure checkout…");
      window.location.assign(checkout.redirectUrl);
    });
  return (
    <section className="mobility-page billing-page">
      <div className="page-heading">
        <div>
          <div className="eyebrow muted">POSTPAID BILLING · BDT</div>
          <h1>Charge now, pay one statement.</h1>
          <p className="subtitle">
            You and your drivers charge first. Every session is added to your
            monthly statement, which you pay through SSLCommerz with bKash,
            Nagad, cards or internet banking. G Energy never holds your money.
          </p>
        </div>
        <Link className="secondary" to="/bookings">
          Charging history ↗
        </Link>
      </div>
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
      {loading ? (
        <p role="status">Loading billing…</p>
      ) : !summary ? (
        <button
          className="secondary"
          onClick={() => {
            setError("");
            void load().catch((e) => setError(e.message));
          }}
        >
          Retry billing
        </button>
      ) : (
        <>
          <div className="billing-overview">
            <section className="panel billing-stat">
              <span className="pill">NOT YET BILLED</span>
              <strong aria-label="Charges not yet billed">
                {taka(summary.unbilledAmount)}
              </strong>
              <p>
                {summary.unbilledSessions} completed session
                {summary.unbilledSessions === 1 ? "" : "s"}
                {summary.activeSessions > 0 &&
                  ` · ${summary.activeSessions} charging now`}
              </p>
            </section>
            <section className="panel billing-stat">
              <span className="pill">TO PAY</span>
              <strong aria-label="Outstanding statements">
                {taka(summary.outstandingAmount)}
              </strong>
              <p>
                {summary.overdueStatements > 0
                  ? `${summary.overdueStatements} overdue statement${summary.overdueStatements === 1 ? "" : "s"}`
                  : `Due ${summary.dueDays} days after each statement`}
              </p>
            </section>
            <section className="panel billing-stat">
              <span className="pill">OWNER PLAN</span>
              <strong>
                {taka(summary.planFeePerVehicle)}
                <small> / car / month</small>
              </strong>
              <p>
                {summary.vehiclesOnPlan} of {summary.vehicles} car
                {summary.vehicles === 1 ? "" : "s"} past the{" "}
                {summary.freeMonths} free months
                {summary.nextTrialEndsUtc &&
                  ` · next trial ends ${date(summary.nextTrialEndsUtc)}`}
              </p>
            </section>
          </div>
          <div className="page-heading">
            <div>
              <h2>Not yet billed</h2>
              <p className="hint">
                Completed charging waits here until your statement is issued on
                the 1st of the month.
              </p>
            </div>
            {summary.canCloseMonth && (
              <button
                className="primary compact"
                disabled={
                  busy ||
                  (summary.unbilledAmount <= 0 && summary.planFeeDue <= 0)
                }
                onClick={() => void closeMonth()}
              >
                Issue statement now (demo)
              </button>
            )}
          </div>
          {unbilled.length === 0 ? (
            <div className="panel empty">
              Nothing waiting. Charging completed by you or your drivers will
              appear here.
            </div>
          ) : (
            <BillingLines lines={unbilled} />
          )}
          <div className="page-heading">
            <h2>Statements</h2>
            <span className="hint">Paid via {summary.gateway}</span>
          </div>
          {statements.length === 0 ? (
            <div className="panel empty">
              No statements yet. Your first one is issued on the 1st of next
              month.
            </div>
          ) : (
            <div className="statement-list">
              {statements.map((s) => (
                <article
                  className={`panel statement-card status-${s.status.toLowerCase()}`}
                  key={s.id}
                >
                  <div className="card-top">
                    <strong>{s.statementNumber}</strong>
                    <span className="pill">{s.status}</span>
                    <strong>{taka(s.total)}</strong>
                  </div>
                  <p>
                    {s.sessionCount} session{s.sessionCount === 1 ? "" : "s"} ·
                    charging {taka(s.chargingTotal)}
                    {s.planFee > 0 &&
                      ` · plan ${taka(s.planFee)} (${s.planVehicleCount} car${s.planVehicleCount === 1 ? "" : "s"})`}
                  </p>
                  <p className="hint">
                    Issued {date(s.issuedAtUtc)} ·{" "}
                    {s.status === "Paid" && s.paidAtUtc
                      ? `Paid ${date(s.paidAtUtc)}`
                      : `Due ${date(s.dueAtUtc)}`}
                  </p>
                  <div className="statement-actions">
                    {s.status !== "Paid" && (
                      <button
                        className="primary compact"
                        disabled={busy}
                        onClick={() => void pay(s)}
                      >
                        Pay {taka(s.total)} with SSLCommerz
                      </button>
                    )}
                    <button
                      className="secondary"
                      disabled={busy}
                      onClick={() =>
                        open?.statement.id === s.id
                          ? setOpen(null)
                          : void run(() => show(s.id))
                      }
                    >
                      {open?.statement.id === s.id ? "Hide details" : "Details"}
                    </button>
                  </div>
                  {open?.statement.id === s.id && (
                    <div className="statement-detail">
                      {open.lines.length > 0 ? (
                        <BillingLines lines={open.lines} />
                      ) : (
                        <p className="hint">Owner plan fee only.</p>
                      )}
                      {open.payments.length > 0 && (
                        <>
                          <h3>Payment attempts</h3>
                          <ul className="payment-attempts">
                            {open.payments.map((p) => (
                              <li key={p.id}>
                                <strong>{p.status}</strong> · {taka(p.amount)} ·{" "}
                                {p.gateway === "SSLCommerzDemo"
                                  ? "SSLCommerz (demo)"
                                  : p.gateway}
                                {p.method && ` · ${methodLabel(p.method)}`} ·{" "}
                                <span className="transaction-reference">
                                  {p.tranId}
                                </span>
                                {p.failureReason && ` · ${p.failureReason}`}
                              </li>
                            ))}
                          </ul>
                        </>
                      )}
                    </div>
                  )}
                </article>
              ))}
            </div>
          )}
          <p className="hint">
            Demo: payments use the {summary.gateway}; no real money moves. Never
            enter real card numbers or PINs.
          </p>
        </>
      )}
    </section>
  );
}
function BillingLines({ lines }: { lines: BillingLine[] }) {
  return (
    <div className="table-scroll">
      <table className="billing-table">
        <thead>
          <tr>
            <th scope="col">Date</th>
            <th scope="col">Station</th>
            <th scope="col">Car</th>
            <th scope="col">Charged by</th>
            <th scope="col">kWh</th>
            <th scope="col">Cost</th>
          </tr>
        </thead>
        <tbody>
          {lines.map((l) => (
            <tr key={l.sessionId}>
              <td>
                <Link to={`/charging-sessions/${l.sessionId}`}>
                  {date(l.startedAtUtc)}
                </Link>
              </td>
              <td>
                {l.stationName} · {l.chargerCode}
              </td>
              <td>{l.vehicleRegistration}</td>
              <td>{l.chargedBy}</td>
              <td>{l.energyKWh.toFixed(2)}</td>
              <td>{taka(l.cost)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
