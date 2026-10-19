import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import {
  useWalletApi,
  type Wallet as WalletData,
  type WalletTransaction,
} from "../features/wallet/api";
export function Wallet() {
  const api = useWalletApi();
  const [wallet, setWallet] = useState<WalletData | null>(null),
    [rows, setRows] = useState<WalletTransaction[]>([]),
    [amount, setAmount] = useState(""),
    [outcome, setOutcome] = useState("Success"),
    [busy, setBusy] = useState(false),
    [loading, setLoading] = useState(true),
    [error, setError] = useState(""),
    [message, setMessage] = useState(""),
    [more, setMore] = useState(false);
  const key = useRef(crypto.randomUUID()),
    submitting = useRef(false);
  function storageKey(id: number) {
    return `g-energy-wallet-pending-${id}`;
  }
  function pending(value?: { amount: string; outcome: string; key: string }) {
    if (!wallet) return;
    try {
      if (value)
        sessionStorage.setItem(storageKey(wallet.id), JSON.stringify(value));
      else sessionStorage.removeItem(storageKey(wallet.id));
    } catch {
      /* In-memory retries remain available when browser storage is disabled. */
    }
  }
  function changeDetails() {
    key.current = crypto.randomUUID();
    pending();
  }
  const load = useCallback(async () => {
    const [w, t] = await Promise.all([api.wallet(), api.transactions()]);
    setWallet(w);
    setRows(t);
    setMore(t.length === 20);
  }, [api]);
  useEffect(() => {
    let active = true;
    Promise.all([api.wallet(), api.transactions()])
      .then(([w, t]) => {
        if (active) {
          setWallet(w);
          setRows(t);
          setMore(t.length === 20);
          try {
            const saved = JSON.parse(
              sessionStorage.getItem(storageKey(w.id)) ?? "null",
            );
            if (
              saved &&
              typeof saved.key === "string" &&
              typeof saved.amount === "string" &&
              ["Success", "Failure"].includes(saved.outcome)
            ) {
              key.current = saved.key;
              setAmount(saved.amount);
              setOutcome(saved.outcome);
              setMessage(
                "An earlier top-up result was uncertain. Check history or retry these details safely with the same reference.",
              );
            }
          } catch {
            /* Ignore unavailable or malformed local retry state. */
          }
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
  }, [api]);
  async function topUp(e: React.FormEvent) {
    e.preventDefault();
    if (submitting.current) return;
    const value = Number(amount);
    setMessage("");
    setError("");
    if (
      !wallet ||
      !Number.isFinite(value) ||
      value <= 0 ||
      value > wallet.maxTransactionAmount ||
      !/^\d+(\.\d{1,2})?$/.test(amount)
    ) {
      setError(
        `Enter a positive amount up to ${wallet?.maxTransactionAmount ?? 100000} BDT with at most two decimal places.`,
      );
      return;
    }
    submitting.current = true;
    setBusy(true);
    pending({ amount, outcome, key: key.current });
    try {
      const result = await api.topUp(value, outcome, key.current);
      pending();
      key.current = crypto.randomUUID();
      if (result.transaction.status === "Failed")
        setError(result.transaction.description);
      else
        setMessage(
          `Demo top-up completed: ${result.transaction.reference}. No real money transferred.`,
        );
      await load();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  }
  async function refund(t: WalletTransaction) {
    if (submitting.current) return;
    submitting.current = true;
    setBusy(true);
    setError("");
    setMessage("");
    try {
      const r = await api.refund(t.id);
      setMessage(
        `Full demo refund completed: ${r.transaction.reference}. Original payment retained.`,
      );
      await load();
    } catch (e) {
      setError((e as Error).message);
      await load().catch(() => {});
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  }
  async function older() {
    if (!rows.length || submitting.current) return;
    submitting.current = true;
    setBusy(true);
    try {
      const next = await api.transactions(rows[rows.length - 1].id);
      setRows((old) => [...old, ...next]);
      setMore(next.length === 20);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  }
  return (
    <section className="mobility-page wallet-page">
      <div className="page-heading">
        <div>
          <div className="eyebrow muted">DEMO WALLET · BDT</div>
          <h1>A balance for your next journey.</h1>
          <p className="subtitle">
            Simulated payments only. Never enter card, banking, PIN or mobile
            payment credentials.
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
        <p role="status">Loading wallet…</p>
      ) : !wallet ? (
        <button
          className="secondary"
          onClick={() => {
            setError("");
            void load().catch((e) => setError(e.message));
          }}
        >
          Retry wallet
        </button>
      ) : (
        <>
          <div className="wallet-overview">
            <section className="panel wallet-balance">
              <span className="pill">SIMULATED FUNDS</span>
              <h2>Available balance</h2>
              <strong aria-label="Available wallet balance">
                {wallet.balance.toFixed(2)} <small>BDT</small>
              </strong>
              <p>
                Completed charging bills are paid automatically at stop when
                funds are available. Unpaid bills can be paid from their session
                page.
              </p>
            </section>
            <form className="panel" onSubmit={topUp} noValidate>
              <h2>Add demo funds</h2>
              <label htmlFor="topup-amount">Amount (BDT)</label>
              <input
                id="topup-amount"
                inputMode="decimal"
                type="number"
                min="0.01"
                step="0.01"
                max={wallet.maxTransactionAmount}
                value={amount}
                disabled={busy}
                onChange={(e) => {
                  changeDetails();
                  setAmount(e.target.value);
                }}
              />
              <p className="hint">
                Up to {wallet.maxTransactionAmount.toFixed(2)} BDT per
                transaction. Wallet limit {wallet.maxBalance.toFixed(2)} BDT.
              </p>
              <label htmlFor="mock-outcome">Simulated payment outcome</label>
              <select
                id="mock-outcome"
                value={outcome}
                disabled={busy}
                onChange={(e) => {
                  changeDetails();
                  setOutcome(e.target.value);
                }}
              >
                <option value="Success">Demo success</option>
                <option value="Failure">Demo failure</option>
              </select>
              <button className="primary compact" disabled={busy}>
                {busy ? "Processing…" : "Add simulated funds"}
              </button>
            </form>
          </div>
          <div className="page-heading">
            <h2>Transaction history</h2>
            <button
              className="secondary"
              disabled={busy}
              onClick={() => {
                setError("");
                void load().catch((e) => setError(e.message));
              }}
            >
              Refresh balance & history
            </button>
          </div>
          {rows.length === 0 ? (
            <div className="panel empty">
              No transactions yet. Your demo top-ups, charging payments and
              refunds will appear here.
            </div>
          ) : (
            <div className="wallet-ledger">
              {rows.map((t) => (
                <article className="panel transaction-card" key={t.id}>
                  <div className="card-top">
                    <strong>
                      {t.type === "ChargingPayment"
                        ? "Charging payment"
                        : t.type === "TopUp"
                          ? "Demo top-up"
                          : "Demo refund"}
                    </strong>
                    <span className="pill">{t.status}</span>
                    <strong>
                      {t.type === "ChargingPayment" ? "−" : "+"}
                      {t.amount.toFixed(2)} {t.currency}
                      {t.status === "Failed" ? " (not credited)" : ""}
                    </strong>
                  </div>
                  <p>{t.description}</p>
                  <p className="hint transaction-reference">
                    {t.reference} · {new Date(t.createdAtUtc).toLocaleString()}
                  </p>
                  {t.chargingSessionId && (
                    <Link to={`/charging-sessions/${t.chargingSessionId}`}>
                      Session #{t.chargingSessionId} ↗
                    </Link>
                  )}
                  {t.refundReference && <p>Refunded · {t.refundReference}</p>}
                  {t.canRefund && (
                    <div>
                      <p className="hint">
                        Demo policy: full one-time reversal of this charging
                        payment. This session stays settled.
                      </p>
                      <button
                        className="secondary"
                        disabled={busy}
                        onClick={() => void refund(t)}
                      >
                        Refund full demo payment
                      </button>
                    </div>
                  )}
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
              Load older transactions
            </button>
          )}
        </>
      )}
    </section>
  );
}
