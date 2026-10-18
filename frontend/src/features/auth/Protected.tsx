import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "./AuthContext";
import { dashboardPath, type Role } from "../../services/api";
export function Protected({ role, roles }: { role?: Role; roles?: Role[] }) {
  const auth = useAuth();
  if (auth.loading)
    return (
      <div className="empty" role="status">
        Checking your account…
      </div>
    );
  if (auth.error)
    return (
      <div className="empty">
        <p role="alert">{auth.error}</p>
        <button onClick={auth.retry}>Try again</button>
        <button onClick={auth.logout}>Log out</button>
      </div>
    );
  if (!auth.user) return <Navigate to="/login" replace />;
  if (!(roles ?? (role ? [role] : [])).includes(auth.user.role))
    return <Navigate to={dashboardPath(auth.user.role)} replace />;
  return <Outlet />;
}
