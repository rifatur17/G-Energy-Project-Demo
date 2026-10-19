import { Link, Outlet } from "react-router-dom";
import { useAuth } from "../features/auth/AuthContext";
import { dashboardPath } from "../services/api";
export function Layout() {
  const { user, logout } = useAuth();
  return (
    <div className="app">
      <header className={user ? "signed-in" : undefined}>
        <Link className="brand" to="/">
          <span className="brand-mark" aria-hidden="true">
            ϟ
          </span>{" "}
          G Energy<span className="demo-tag">DEMO</span>
        </Link>
        <nav aria-label="Main navigation">
          {user ? (
            <>
              <Link to={dashboardPath(user.role)}>Dashboard</Link>
              {user.role !== "Admin" && <Link to="/vehicles">Vehicles</Link>}
              <Link to="/stations">Stations</Link>
              {user.role !== "Admin" && (
                <Link to="/bookings">Bookings & charging</Link>
              )}
              {user.role === "CarOwner" && <Link to="/wallet">Wallet</Link>}
              <button className="text-button" onClick={logout}>
                Log out
              </button>
            </>
          ) : (
            <>
              <Link to="/login">Log in</Link>
              <Link className="nav-cta" to="/signup">
                Create account ↗
              </Link>
            </>
          )}
        </nav>
      </header>
      <main>
        <Outlet />
      </main>
      <footer>
        <span>G Energy · Smart charging. Smarter journeys.</span>
        <span>EV charging demonstration · Bangladesh</span>
      </footer>
    </div>
  );
}
