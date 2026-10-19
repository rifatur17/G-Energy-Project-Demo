import { useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { useWalletApi } from "./api";
import type { Session } from "../charging/api";
export function SessionPayment({
  session,
  onPaid,
}: {
  session: Session;
  onPaid: (reference: string) => void;
}) {
  const { user } = useAuth();
  if (
    user?.role !== "CarOwner" ||
    user.id !== session.payerOwnerUserId ||
    session.status !== "Completed" ||
    session.paymentStatus !== "Unpaid"
  )
    return null;
  return <OwnerPayment session={session} onPaid={onPaid} />;
}
function OwnerPayment({
  session,
  onPaid,
}: {
  session: Session;
  onPaid: (reference: string) => void;
}) {
  const api = useWalletApi();
  const [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const submitting = useRef(false);
  async function pay() {
    if (submitting.current) return;
    submitting.current = true;
    setBusy(true);
    setError("");
    try {
      const result = await api.pay(session.id);
      onPaid(result.transaction.reference);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  }
  return (
    <section className="session-payment">
      <h2>Pay this completed session</h2>
      <p>
        Your final bill is {session.cost.toFixed(2)} {session.currency}. Payment
        uses your Owner demo wallet.
      </p>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      <div className="action-row">
        <button
          className="primary compact"
          disabled={busy || session.cost <= 0}
          onClick={() => void pay()}
        >
          {busy ? "Paying…" : "Pay from demo wallet"}
        </button>
        <Link className="secondary" to="/wallet">
          Check balance / add demo funds ↗
        </Link>
      </div>
    </section>
  );
}
