import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import {
  useWalletApi,
  type TopUpAttempt,
  type Wallet as WalletData,
  type WalletTransaction,
} from "../features/wallet/api";
import { methodLabel } from "../features/billing/api";
const topUpResults: Record<
  string,
  { kind: "success" | "error"; text: string }
> = {
  paid: {
    kind: "success",
    text: "Payment confirmed by SSLCommerz. The money has been added to your wallet.",
  },
  unavailable: {
    kind: "error",
    text: "This payment service is not available at the moment. Nothing was charged and your balance is unchanged.",
  },
  failed: {
    kind: "error",
    text: "The payment failed at SSLCommerz. Nothing was charged; you can try again.",
  },
  cancelled: {
    kind: "error",
    text: "You cancelled the SSLCommerz checkout. Your balance is unchanged.",
  },
  unconfirmed: {
    kind: "error",
    text: "SSLCommerz did not confirm this payment, so nothing was added to your wallet.",
  },
};
export function Wallet() {
  const api = useWalletApi();
  const [params, setParams] = useSearchParams();
  const [wallet, setWallet] = useState<WalletData | null>(null),
    [rows, setRows] = useState<WalletTransaction[]>([]),
    [attempts, setAttempts] = useState<TopUpAttempt[]>([]),
    [amount, setAmount] = useState(""),
    [busy, setBusy] = useState(false),
    [loading, setLoading] = useState(true),
    [error, setError] = useState(""),
    [message, setMessage] = useState(""),
    [more, setMore] = useState(false);
  const submitting = useRef(false);
  const load = useCallback(async () => {
    const [w, t, a] = await Promise.all([
      api.wallet(),
      api.transactions(),
      api.topUps().catch(() => [] as TopUpAttempt[]),
    ]);
    setWallet(w);
    setRows(t);
    setMore(t.length === 20);
    setAttempts(a);
  }, [api]);
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
    const result = params.get("topup");
    if (!result) return;
    const known = topUpResults[result];
    if (known?.kind === "success") setMessage(known.text);
    else setError(known?.text ?? "Top-up result is unknown.");
    setParams({}, { replace: true });
  }, [params, setParams]);
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
    try {
      const checkout = await api.topUpCheckout(value);
      setMessage("Opening SSLCommerz secure checkout…");
      window.location.assign(checkout.redirectUrl);
    } catch (e) {
      setError((e as Error).message);
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
          <div className="eyebrow muted">OWNER WALLET · BDT</div>
          <h1>A balance for your next journey.</h1>
          <p className="subtitle">
            Add money through SSLCommerz with bKash, Nagad, cards or internet
            banking. Charging is paid from this balance automatically.
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
              <span className="pill">WALLET BALANCE</span>
              <h2>Available balance</h2>
              <strong aria-label="Available wallet balance">
                {wallet.balance.toFixed(2)} <small>BDT</small>
              </strong>
              <p>
                Charging bills are paid automatically when charging stops. If
                the balance is too low, the bill stays unpaid: pay it from the
                session page after adding money, or it goes on your monthly{" "}
                <Link to="/billing">statement</Link>.
              </p>
            </section>
            <form className="panel" onSubmit={topUp} noValidate>
              <h2>Add money</h2>
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
                onChange={(e) => setAmount(e.target.value)}
              />
              <p className="hint">
                Up to {wallet.maxTransactionAmount.toFixed(2)} BDT per top-up.
                Wallet limit {wallet.maxBalance.toFixed(2)} BDT. You choose
                bKash, Nagad, a card or internet banking on the SSLCommerz page.
              </p>
              <button className="primary compact" disabled={busy}>
                {busy ? "Opening checkout…" : "Add money with SSLCommerz"}
              </button>
              <p className="hint">
                Demo: no real money moves. Never enter real card numbers or
                PINs.
              </p>
            </form>
          </div>
          {attempts.length > 0 && (
            <>
              <h2>Recent top-up attempts</h2>
              <ul className="payment-attempts">
                {attempts.map((a) => (
                  <li key={a.id}>
                    <strong>{a.status}</strong> · {a.amount.toFixed(2)} BDT ·{" "}
                    {a.gateway === "SSLCommerzDemo"
                      ? "SSLCommerz (demo)"
                      : a.gateway}
                    {a.method && ` · ${methodLabel(a.method)}`} ·{" "}
                    <span className="transaction-reference">{a.tranId}</span>
                    {a.failureReason && ` · ${a.failureReason}`}
                  </li>
                ))}
              </ul>
            </>
          )}
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
              No transactions yet. Your top-ups, charging payments and refunds
              will appear here.
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
                          ? "Top-up"
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
