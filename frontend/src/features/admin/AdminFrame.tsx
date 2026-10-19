import type { ReactNode } from "react";
import { NavLink } from "react-router-dom";
export function AdminFrame({
  title,
  subtitle,
  children,
}: {
  title: string;
  subtitle: string;
  children: ReactNode;
}) {
  return (
    <section className="mobility-page admin-page">
      <div className="eyebrow muted">ADMIN OPERATIONS · G ENERGY DEMO</div>
      <div className="page-heading">
        <div>
          <h1>{title}</h1>
          <p className="subtitle">{subtitle}</p>
        </div>
        <span className="pill">SIMULATED NETWORK</span>
      </div>
      <nav className="admin-nav" aria-label="Admin navigation">
        {[
          ["/dashboard/admin", "Overview"],
          ["/admin/stations", "Network"],
          ["/admin/bookings", "Bookings"],
          ["/admin/charging-sessions", "Sessions"],
          ["/admin/wallet-transactions", "Transactions"],
          ["/admin/wallets", "Wallets"],
          ["/admin/users", "Users"],
        ].map(([to, label]) => (
          <NavLink key={to} to={to}>
            {label}
          </NavLink>
        ))}
      </nav>
      {children}
    </section>
  );
}
