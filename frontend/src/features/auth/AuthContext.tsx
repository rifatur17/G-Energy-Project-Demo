import {
  createContext,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from "react";
import { api, type Session, type User } from "../../services/api";
interface Auth {
  accessToken: string | null;
  user: User | null;
  loading: boolean;
  error: string;
  login: (code: string, password: string) => Promise<User>;
  logout: () => void;
  retry: () => void;
}
const Context = createContext<Auth | null>(null);
const key = "g-energy-session";
function readSession(): Session | null {
  try {
    const value = JSON.parse(sessionStorage.getItem(key) || "null");
    return value?.accessToken && Date.parse(value.expiresAtUtc) > Date.now()
      ? value
      : null;
  } catch {
    return null;
  }
}
export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(readSession);
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(!!session);
  const [error, setError] = useState("");
  const [attempt, setAttempt] = useState(0);
  const logout = () => {
    sessionStorage.removeItem(key);
    setSession(null);
    setUser(null);
    setError("");
    setLoading(false);
  };
  useEffect(() => {
    let active = true;
    if (!session) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError("");
    api
      .me(session.accessToken)
      .then((u) => {
        if (active) setUser(u);
      })
      .catch((e) => {
        if (!active) return;
        if (e.status === 401 || e.status === 403) logout();
        else setError(e.message);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    const timer = window.setTimeout(
      logout,
      Math.max(0, Date.parse(session.expiresAtUtc) - Date.now()),
    );
    return () => {
      active = false;
      window.clearTimeout(timer);
    };
  }, [session, attempt]);
  const login = async (code: string, password: string) => {
    const next = await api.login(code, password);
    const profile = await api.me(next.accessToken);
    sessionStorage.setItem(key, JSON.stringify(next));
    setSession(next);
    setUser(profile);
    return profile;
  };
  return (
    <Context.Provider
      value={{
        accessToken: session?.accessToken ?? null,
        user,
        loading,
        error,
        login,
        logout,
        retry: () => setAttempt((a) => a + 1),
      }}
    >
      {children}
    </Context.Provider>
  );
}
export function useAuth() {
  const value = useContext(Context);
  if (!value) throw new Error("AuthProvider required");
  return value;
}
