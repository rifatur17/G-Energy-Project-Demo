// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import "@testing-library/jest-dom/vitest";
import { MemoryRouter } from "react-router-dom";
import App from "../App";
import { AuthProvider } from "../features/auth/AuthContext";
import { api, ApiError, type Role, type User } from "../services/api";
vi.mock("../services/api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../services/api")>()),
  api: { register: vi.fn(), login: vi.fn(), me: vi.fn() },
}));
vi.mock("../features/admin/api", () => ({
  useAdminApi: () => ({ dashboard: async () => null }),
}));
const owner: User = {
  id: 1,
  userCode: "GE-OWN-001001",
  fullName: "Demo Owner",
  email: "owner@example.test",
  phone: "01700000000",
  role: "CarOwner",
  createdAtUtc: new Date().toISOString(),
};
const session = {
  accessToken: "test-token",
  userCode: owner.userCode,
  role: owner.role,
  expiresAtUtc: new Date(Date.now() + 1800000).toISOString(),
};
function show(path = "/login") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <App />
      </AuthProvider>
    </MemoryRouter>,
  );
}
beforeEach(() => {
  sessionStorage.clear();
  vi.resetAllMocks();
  vi.mocked(api.me).mockResolvedValue(owner);
});
afterEach(cleanup);
describe("authentication screens", () => {
  it("registers a driver and shows a copyable generated ID", async () => {
    const user = userEvent.setup();
    vi.mocked(api.register).mockResolvedValue({
      ...owner,
      userCode: "GE-DRV-001002",
      role: "CarDriver",
    });
    show("/signup");
    expect(
      screen.queryByRole("button", { name: /Admin/ }),
    ).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Car Driver/ }));
    await user.type(screen.getByLabelText("Full name"), "Demo Driver");
    await user.type(
      screen.getByLabelText("Email address"),
      "driver@example.test",
    );
    await user.type(screen.getByLabelText("Phone number"), "01700000000");
    await user.type(
      screen.getByLabelText("Password"),
      "Test-only-long-passphrase",
    );
    await user.click(screen.getByRole("button", { name: /Create account/ }));
    expect(await screen.findByText("GE-DRV-001002")).toBeVisible();
    expect(screen.getByRole("status", {name: "Welcome email"})).toHaveTextContent("Welcome email is not set up yet");
    expect(screen.getByText(/your password is never included in email/)).toBeVisible();
    expect(api.register).toHaveBeenCalledWith(
      expect.objectContaining({
        role: "CarDriver",
        email: "driver@example.test",
      }),
    );
    await user.click(screen.getByRole("button", { name: "Copy ID" }));
    expect(await screen.findByText("Copied to clipboard")).toBeVisible();
    expect(
      screen.getByRole("link", { name: /Continue to login/ }),
    ).toHaveAttribute("href", "/login");
  });
  it("shows a queued welcome email without claiming it was delivered", async () => {
    const user = userEvent.setup();
    vi.mocked(api.register).mockResolvedValue({...owner, welcomeEmailStatus: "Queued"});
    show("/signup");
    await user.type(screen.getByLabelText("Full name"), owner.fullName);
    await user.type(screen.getByLabelText("Email address"), owner.email);
    await user.type(screen.getByLabelText("Phone number"), owner.phone);
    await user.type(screen.getByLabelText("Password"), "Test-only-long-passphrase");
    await user.click(screen.getByRole("button", {name: /Create account/}));
    expect(await screen.findByRole("status", {name: "Welcome email"})).toHaveTextContent(`queued for ${owner.email}`);
    expect(screen.getByText(owner.userCode)).toBeVisible();
    expect(screen.queryByText(/email has been sent/i)).not.toBeInTheDocument();
  });
  it("shows registration errors and lets the user retry", async () => {
    const user = userEvent.setup();
    vi.mocked(api.register).mockRejectedValue(
      new ApiError("Email is already registered.", 409),
    );
    show("/signup");
    await user.type(screen.getByLabelText("Full name"), "Demo Owner");
    await user.type(
      screen.getByLabelText("Email address"),
      "owner@example.test",
    );
    await user.type(screen.getByLabelText("Phone number"), "01700000000");
    await user.type(
      screen.getByLabelText("Password"),
      "Test-only-long-passphrase",
    );
    await user.click(screen.getByRole("button", { name: /Create account/ }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Email is already registered.",
    );
    expect(
      screen.getByRole("button", { name: /Create account/ }),
    ).toBeEnabled();
  });
  it("logs in using UserCode, validates the profile, routes and logs out", async () => {
    const user = userEvent.setup();
    vi.mocked(api.login).mockResolvedValue(session);
    show();
    await user.type(screen.getByLabelText("User ID"), owner.userCode);
    await user.type(
      screen.getByLabelText("Password"),
      "Test-only-long-passphrase",
    );
    await user.click(screen.getByRole("button", { name: "Log in" }));
    expect(
      await screen.findByRole("heading", { name: "Welcome, Demo Owner." }),
    ).toBeVisible();
    expect(api.login).toHaveBeenCalledWith(
      owner.userCode,
      "Test-only-long-passphrase",
    );
    expect(api.me).toHaveBeenCalledWith("test-token");
    await user.click(screen.getByRole("button", { name: "Log out" }));
    expect(
      await screen.findByRole("heading", { name: "Log in to G Energy" }),
    ).toBeVisible();
    expect(sessionStorage.getItem("g-energy-session")).toBeNull();
  });
  it("displays login errors without revealing a dashboard", async () => {
    const user = userEvent.setup();
    vi.mocked(api.login).mockRejectedValue(
      new ApiError("Invalid UserCode or password.", 401),
    );
    show();
    await user.type(screen.getByLabelText("User ID"), "unknown");
    await user.type(screen.getByLabelText("Password"), "wrong");
    await user.click(screen.getByRole("button", { name: "Log in" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Invalid UserCode or password.",
    );
    expect(screen.queryByText("Your workspace")).not.toBeInTheDocument();
  });
  it.each([
    ["Admin", "admin"],
    ["CarOwner", "owner"],
    ["CarDriver", "driver"],
  ] as [Role, string][])(
    "routes restored %s profiles using the server role",
    async (role, route) => {
      sessionStorage.setItem(
        "g-energy-session",
        JSON.stringify({ ...session, role: "Admin" }),
      );
      vi.mocked(api.me).mockResolvedValue({ ...owner, role });
      show("/dashboard/admin");
      expect(await screen.findByText("Your workspace")).toBeVisible();
      expect(screen.getByRole("link", { name: "Dashboard" })).toHaveAttribute(
        "href",
        `/dashboard/${route}`,
      );
    },
  );
  it("redirects an unauthenticated protected route to login", async () => {
    show("/dashboard/owner");
    expect(
      await screen.findByRole("heading", { name: "Log in to G Energy" }),
    ).toBeVisible();
  });
  it("clears rejected sessions and prevents dashboard access", async () => {
    sessionStorage.setItem("g-energy-session", JSON.stringify(session));
    vi.mocked(api.me).mockRejectedValue(new ApiError("Unauthorized", 401));
    show("/dashboard/owner");
    expect(
      await screen.findByRole("heading", { name: "Log in to G Energy" }),
    ).toBeVisible();
    expect(sessionStorage.getItem("g-energy-session")).toBeNull();
  });
  it("keeps a retry action when profile verification cannot reach the server", async () => {
    const user = userEvent.setup();
    sessionStorage.setItem("g-energy-session", JSON.stringify(session));
    vi.mocked(api.me)
      .mockRejectedValueOnce(new ApiError("Cannot reach G Energy.", 0))
      .mockResolvedValue(owner);
    show("/dashboard/owner");
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Cannot reach G Energy.",
    );
    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByText("Your workspace")).toBeVisible();
  });
  it("automatically logs out when a token expires", async () => {
    sessionStorage.setItem(
      "g-energy-session",
      JSON.stringify({
        ...session,
        expiresAtUtc: new Date(Date.now() + 500).toISOString(),
      }),
    );
    show("/dashboard/owner");
    await screen.findByText("Your workspace");
    await waitFor(
      () =>
        expect(
          screen.getByRole("heading", { name: "Log in to G Energy" }),
        ).toBeVisible(),
      { timeout: 2000 },
    );
    expect(sessionStorage.getItem("g-energy-session")).toBeNull();
  });
});
