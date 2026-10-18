import { useState, type FormEvent } from "react";
import { Link, Navigate } from "react-router-dom";
import { useAuth } from "../features/auth/AuthContext";
import { api, dashboardPath, type User } from "../services/api";
import { Intro } from "../components/AuthIntro";
import { CopyCode } from "../components/CopyCode";
export function Signup() {
  const auth = useAuth();
  const [role, setRole] = useState<"CarOwner" | "CarDriver">("CarOwner");
  const [created, setCreated] = useState<User | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  if (auth.user) return <Navigate to={dashboardPath(auth.user.role)} replace />;
  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const data = new FormData(e.currentTarget);
    setBusy(true);
    setError("");
    try {
      setCreated(
        await api.register({
          fullName: String(data.get("fullName")).trim(),
          email: String(data.get("email")).trim(),
          phone: String(data.get("phone")).trim(),
          password: String(data.get("password")),
          role,
        }),
      );
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  if (created)
    return (
      <section className="success-panel">
        <div className="success-icon" aria-hidden="true">
          ✓
        </div>
        <div className="eyebrow muted">YOU’RE ALL SET</div>
        <h1>Welcome, {created.fullName}.</h1>
        <p>
          Your {created.role === "CarOwner" ? "Car Owner" : "Car Driver"}{" "}
          account is ready. Save your unique User ID before continuing.
        </p>
        <CopyCode code={created.userCode} />
        <Link className="primary" to="/login">
          Continue to login →
        </Link>
        <p className="hint">
          Log in with this ID and the password you just created.
        </p>
      </section>
    );
  return (
    <div className="auth-grid">
      <Intro signup />
      <section className="form-panel signup">
        <div className="eyebrow muted">JOIN THE JOURNEY</div>
        <h2>Create your account</h2>
        <p className="subtitle">Choose how you’ll use G Energy.</p>
        <div className="role-picker" role="group" aria-label="Account role">
          <button
            aria-pressed={role === "CarOwner"}
            className={role === "CarOwner" ? "selected" : ""}
            onClick={() => setRole("CarOwner")}
          >
            Car Owner<span>Manage your vehicles</span>
          </button>
          <button
            aria-pressed={role === "CarDriver"}
            className={role === "CarDriver" ? "selected" : ""}
            onClick={() => setRole("CarDriver")}
          >
            Car Driver<span>Drive assigned vehicles</span>
          </button>
        </div>
        <form onSubmit={submit}>
          <label htmlFor="fullName">Full name</label>
          <input
            id="fullName"
            name="fullName"
            autoComplete="name"
            minLength={2}
            maxLength={100}
            required
          />
          <div className="form-row">
            <div>
              <label htmlFor="email">Email address</label>
              <input
                id="email"
                name="email"
                type="email"
                autoComplete="email"
                maxLength={254}
                required
              />
            </div>
            <div>
              <label htmlFor="phone">Phone number</label>
              <input
                id="phone"
                name="phone"
                type="tel"
                autoComplete="tel"
                pattern="\+?[0-9 \(\)\-]{7,20}"
                maxLength={20}
                required
              />
            </div>
          </div>
          <label htmlFor="newPassword">Password</label>
          <input
            id="newPassword"
            name="password"
            type="password"
            autoComplete="new-password"
            minLength={12}
            maxLength={128}
            required
          />
          <p className="hint">
            Use 12–128 characters. A long, unique passphrase works well.
          </p>
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
          <button className="primary" disabled={busy}>
            {busy ? "Creating account…" : "Create account"}{" "}
            <span aria-hidden="true">→</span>
          </button>
        </form>
        <p className="form-switch">
          Already have an account? <Link to="/login">Log in</Link>
        </p>
      </section>
    </div>
  );
}
