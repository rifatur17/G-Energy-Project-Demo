import { Link, Navigate, Route, Routes } from "react-router-dom";
import { useAuth } from "./features/auth/AuthContext";
import { dashboardPath, type Role } from "./services/api";
import { Layout } from "./layouts/Layout";
import { Protected } from "./features/auth/Protected";
import { Login } from "./pages/Login";
import { Signup } from "./pages/Signup";
import { Dashboard } from "./pages/Dashboard";
function Home() {
  const { user, loading } = useAuth();
  return loading ? (
    <div className="empty" role="status">
      Checking your account…
    </div>
  ) : (
    <Navigate to={user ? dashboardPath(user.role) : "/login"} replace />
  );
}
export default function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Home />} />
        <Route path="login" element={<Login />} />
        <Route path="signup" element={<Signup />} />
        {(["Admin", "CarOwner", "CarDriver"] as Role[]).map((role) => (
          <Route key={role} element={<Protected role={role} />}>
            <Route
              path={dashboardPath(role)}
              element={<Dashboard role={role} />}
            />
          </Route>
        ))}
        <Route
          path="*"
          element={
            <section className="empty">
              <h1>Page not found</h1>
              <Link to="/">Return to G Energy</Link>
            </section>
          }
        />
      </Route>
    </Routes>
  );
}
