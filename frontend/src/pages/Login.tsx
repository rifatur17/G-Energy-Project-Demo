import { useState, type FormEvent } from "react";
import { Link, Navigate, useNavigate } from "react-router-dom";
import { useAuth } from "../features/auth/AuthContext";
import { dashboardPath } from "../services/api";
import { Intro } from "../components/AuthIntro";
export function Login() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  if (auth.user) return <Navigate to={dashboardPath(auth.user.role)} replace />;
  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const data = new FormData(e.currentTarget);
    setBusy(true);
    setError("");
    try {
      const u = await auth.login(
        String(data.get("userCode")).trim(),
        String(data.get("password")),
      );
      navigate(dashboardPath(u.role), { replace: true });
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="auth-grid">
      <Intro />
      <section className="form-panel">
        <div className="eyebrow muted">WELCOME BACK</div>
        <h2>Log in to G Energy</h2>
        <p className="subtitle">Your account. Your electric journey.</p>
        <form onSubmit={submit}>
          <label htmlFor="userCode">User ID</label>
          <input
            id="userCode"
            name="userCode"
            autoComplete="username"
            placeholder="GE-OWN-001001"
            maxLength={24}
            required
          />
          <p className="hint">Use the unique ID you received at signup.</p>
          <label htmlFor="password">Password</label>
          <input
            id="password"
            name="password"
            type="password"
            autoComplete="current-password"
            maxLength={128}
            required
          />
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
          <button className="primary" disabled={busy}>
            {busy ? "Logging in…" : "Log in"} <span aria-hidden="true">→</span>
          </button>
        </form>
        <p className="form-switch">
          New to G Energy? <Link to="/signup">Create an account</Link>
        </p>
        <div className="info-note">
          Keep your User ID handy. You’ll use it every time you log in.
        </div>
      </section>
    </div>
  );
}
