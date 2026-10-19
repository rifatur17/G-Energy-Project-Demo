import { Link } from "react-router-dom";
import { useAuth } from "../features/auth/AuthContext";
import { type Role } from "../services/api";
import { CopyCode } from "../components/CopyCode";
const modules: Record<
  Role,
  { title: string; description: string; owner: string }[]
> = {
  CarOwner: [
    {
      title: "Your vehicles",
      description: "Vehicle management and driver assignments.",
      owner: "Member 2",
    },
    {
      title: "Find a charging point",
      description: "Station discovery, compatibility and availability.",
      owner: "Member 2",
    },
    {
      title: "Your charging journey",
      description: "Reservations and simulated charging sessions.",
      owner: "Member 3",
    },
    {
      title: "Wallet & receipts",
      description: "Mock top-ups, billing and payment history.",
      owner: "Member 4",
    },
  ],
  CarDriver: [
    {
      title: "Assigned vehicles",
      description: "Vehicles shared with you by their owners.",
      owner: "Member 2",
    },
    {
      title: "Find a charging point",
      description: "Station discovery and compatible chargers.",
      owner: "Member 2",
    },
    {
      title: "Your charging sessions",
      description: "Reservations and charging with owner permission.",
      owner: "Member 3",
    },
  ],
  Admin: [
    {
      title: "Network management",
      description: "Station and charging point administration.",
      owner: "Member 5",
    },
    {
      title: "Activity overview",
      description: "Bookings, users and paid charging revenue.",
      owner: "Member 5",
    },
  ],
};
export function Dashboard({ role }: { role: Role }) {
  const { user } = useAuth();
  if (!user) return null;
  return (
    <section className="dashboard">
      <div className="dashboard-heading">
        <div>
          <div className="eyebrow muted">
            {role === "CarOwner"
              ? "CAR OWNER"
              : role === "CarDriver"
                ? "CAR DRIVER"
                : "ADMIN"}{" "}
            WORKSPACE
          </div>
          <h1>Welcome, {user.fullName}.</h1>
          <p className="subtitle">
            You’re connected. Your journey starts with your account.
          </p>
        </div>
        <span className="account-badge">● Account active</span>
      </div>
      <div className="dashboard-content">
        <section className="identity-card">
          <h2>Your G Energy identity</h2>
          <p>Share this User ID when coordinating access with your team.</p>
          <CopyCode code={user.userCode} />
          <dl>
            <dt>Email</dt>
            <dd>{user.email}</dd>
            <dt>Account type</dt>
            <dd>
              {role === "CarOwner"
                ? "Car Owner"
                : role === "CarDriver"
                  ? "Car Driver"
                  : "Administrator"}
            </dd>
          </dl>
        </section>
        <section>
          <h2 className="section-title">Your workspace</h2>
          <p className="subtitle">
            Manage vehicles, discover stations and follow your charging journey.
          </p>
          <div className="module-grid">
            {modules[role].map((m) => (
              <article className="module-card" key={m.title}>
                <span className="module-status">
                  {m.owner === "Member 2" || m.owner === "Member 3"
                    ? "READY TO EXPLORE"
                    : "AWAITING INTEGRATION"}
                </span>
                <h3>
                  {m.owner === "Member 2" || m.owner === "Member 3" ? (
                    <Link
                      to={
                        m.owner === "Member 3"
                          ? "/bookings"
                          : m.title === "Find a charging point"
                            ? "/stations"
                            : "/vehicles"
                      }
                    >
                      {m.title} ↗
                    </Link>
                  ) : (
                    m.title
                  )}
                </h3>
                <p>{m.description}</p>
                <span className="hint">{m.owner}</span>
              </article>
            ))}
          </div>
        </section>
      </div>
    </section>
  );
}
