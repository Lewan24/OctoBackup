import { Component, useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import {
  NavLink,
  Navigate,
  Route,
  Routes,
  useLocation,
} from "react-router-dom";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import {
  Database,
  LayoutDashboard,
  Server,
  CalendarClock,
  Archive,
  Users,
  Settings,
  LogOut,
  Menu,
  ArrowUpRight,
  Radio,
  X,
  ListOrdered,
} from "lucide-react";
import { api, ApiError, resetCsrf } from "../shared/api";
import type { CurrentUser } from "../shared/types";
import { ErrorMessage, Loading } from "../shared/ui";
import { AuthPage } from "../features/auth/AuthPage";
import { Dashboard } from "../features/dashboard/Dashboard";
import { ServersPage } from "../features/servers/ServersPage";
import { SchedulesPage } from "../features/schedules/SchedulesPage";
import { BackupsPage } from "../features/backups/BackupsPage";
import { AccountPage } from "../features/account/AccountPage";
import { UsersPage } from "../features/admin/UsersPage";
import { ApplicationSettingsPage } from "../features/admin/ApplicationSettingsPage";
import { QueueHero } from "../features/queue/QueueHero";
import { QueuePage } from "../features/queue/QueuePage";

class ErrorBoundary extends Component<
  { children: ReactNode },
  { error: boolean }
> {
  state = { error: false };
  static getDerivedStateFromError() {
    return { error: true };
  }
  render() {
    return this.state.error ? (
      <main className="fatal">
        <h1>Something went wrong</h1>
        <p>Reload the workspace to try again.</p>
        <button onClick={() => window.location.reload()}>
          Reload workspace
        </button>
      </main>
    ) : (
      this.props.children
    );
  }
}
export function App() {
  return (
    <ErrorBoundary>
      <Workspace />
    </ErrorBoundary>
  );
}
function Workspace() {
  const client = useQueryClient();
  const location = useLocation();
  const [open, setOpen] = useState(false);
  const menuButton = useRef<HTMLButtonElement>(null);
  const [liveState, setLiveState] = useState("Connecting");
  const [logoutError, setLogoutError] = useState<unknown>();
  const session = useQuery({
    queryKey: ["session"],
    queryFn: ({ signal }) =>
      api<CurrentUser>("auth/GetCurrentUser", undefined, signal),
    refetchInterval: 60_000,
  });
  const authenticated = session.data?.isAuthenticated ?? false;
  const admin = Object.entries(session.data?.claims ?? {}).some(
    ([key, value]) =>
      key.endsWith("/role") &&
      (() => {
        try {
          return (JSON.parse(value) as string[]).includes("Admin");
        } catch {
          return false;
        }
      })(),
  );
  useEffect(() => {
    setOpen(false);
    document.title = `${location.pathname.split("/")[1] || "Dashboard"} · OctoBackup`;
  }, [location.pathname]);
  useEffect(() => {
    if (!open) return;
    function closeOnEscape(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setOpen(false);
        menuButton.current?.focus();
      }
    }
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [open]);
  useEffect(() => {
    return client.getQueryCache().subscribe((event) => {
      if (
        event.type === "updated" &&
        event.query.state.error instanceof ApiError &&
        event.query.state.error.status === 401
      ) {
        client.setQueryData(["session"], { isAuthenticated: false });
      }
    });
  }, [client]);
  useEffect(() => {
    if (!authenticated) return;
    const hub = new HubConnectionBuilder()
      .withUrl("/backuphub")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Error)
      .build();
    const refresh = () => {
      void client.invalidateQueries({
        predicate: (q) =>
          !["session", "application-settings", "registration-policy"].includes(
            String(q.queryKey[0]),
          ),
      });
    };
    for (const event of [
      "ServerCreatedEvent",
      "ServerHasChangedEvent",
      "ScheduleCreatedEvent",
      "ScheduleHasChangedEvent",
      "BackupCreatedEvent",
      "BackupQueueChangedEvent",
    ])
      hub.on(event, refresh);
    hub.onreconnecting(() => setLiveState("Reconnecting"));
    hub.onclose(() => setLiveState("Updates offline"));
    hub.onreconnected(() => {
      setLiveState("Live updates");
      refresh();
    });
    setLiveState("Connecting");
    void hub
      .start()
      .then(() => setLiveState("Live updates"))
      .catch(() => setLiveState("Updates offline"));
    return () => {
      void hub.stop();
    };
  }, [authenticated, client]);
  if (session.isPending) return <Loading />;
  if (session.isError)
    return (
      <main className="fatal">
        <ErrorMessage error={session.error} />
        <button onClick={() => void session.refetch()}>Try again</button>
      </main>
    );
  if (!authenticated)
    return (
      <AuthPage
        onLogin={async () => {
          client.removeQueries({
            predicate: (q) => q.queryKey[0] !== "session",
          });
          await session.refetch();
        }}
      />
    );
  const navigation = [
    { to: "/", label: "Overview", icon: LayoutDashboard },
    { to: "/servers", label: "Servers", icon: Server },
    { to: "/queue", label: "Backup queue", icon: ListOrdered },
    { to: "/schedules", label: "Schedules", icon: CalendarClock },
    { to: "/backups", label: "Backup history", icon: Archive },
  ];
  return (
    <div className="app-shell">
      <a className="skip" href="#main">
        Skip to content
      </a>
      <aside
        id="workspace-navigation"
        className={`sidebar ${open ? "is-open" : ""}`}
      >
        <NavLink to="/" className="brand">
          <Database /> OctoBackup
          <span className="brand-dot" />
        </NavLink>
        <div className="workspace-label">
          <span className="workspace-avatar">
            <Server size={16} />
          </span>
          <div>
            Backup workspace<small>Self-hosted infrastructure</small>
          </div>
        </div>
        <nav aria-label="Main navigation">
          {navigation.map(({ to, label, icon: Icon }) => (
            <NavLink key={to} to={to} end={to === "/"}>
              <Icon size={19} />
              {label}
            </NavLink>
          ))}
          {admin && (
            <>
              <span className="nav-section">Administration</span>
              <NavLink to="/users">
                <Users size={19} />
                Users & access
              </NavLink>
              <NavLink to="/settings">
                <Settings size={19} />
                Application settings
              </NavLink>
            </>
          )}
          <NavLink to="/account">
            <Settings size={19} />
            Account settings
          </NavLink>
        </nav>
        <div className="sidebar-footer">
          <div className="side-note">
            <ShieldMark />
            <p>
              Your infrastructure.
              <small>Connections. Copies. Control.</small>
            </p>
          </div>
          <button
            className="signout"
            onClick={async () => {
              try {
                await api("auth/Logout", {});
                resetCsrf();
                client.clear();
                window.location.assign("/");
              } catch (e) {
                setLogoutError(e);
              }
            }}
          >
            <LogOut size={18} />
            Sign out
          </button>
        </div>
      </aside>
      {open && (
        <button
          className="navigation-backdrop"
          aria-label="Close navigation"
          onClick={() => {
            setOpen(false);
            menuButton.current?.focus();
          }}
        />
      )}
      <div className="workspace">
        <header className="topbar">
          <button
            className="icon-button mobile-menu"
            ref={menuButton}
            aria-label="Toggle navigation"
            aria-controls="workspace-navigation"
            aria-expanded={open}
            onClick={() => setOpen(!open)}
          >
            {open ? <X /> : <Menu />}
          </button>
          <span className="breadcrumb">
            Workspace <span>/</span>{" "}
            {navigation.find((n) => n.to === location.pathname)?.label ??
              (location.pathname === "/users"
                ? "Users & access"
                : location.pathname === "/settings"
                  ? "Application settings"
                  : "Account")}
          </span>
          <span
            className={`live-status ${liveState === "Live updates" ? "connected" : ""}`}
            role="status"
          >
            <Radio size={13} />
            {liveState}
          </span>
          <NavLink to="/account" className="profile">
            <span className="avatar">
              {session.data?.userName?.slice(0, 1).toUpperCase()}
            </span>
            <span>
              {session.data?.userName}
              <small>{admin ? "Administrator" : "Operator"}</small>
            </span>
            <ArrowUpRight size={16} />
          </NavLink>
        </header>
        <main id="main" className="content">
          <ErrorMessage error={logoutError} />
          <QueueHero />
          <Routes>
            <Route path="/" element={<Dashboard admin={admin} />} />
            <Route path="/servers" element={<ServersPage admin={admin} />} />
            <Route path="/schedules" element={<SchedulesPage />} />
            <Route path="/backups" element={<BackupsPage />} />
            <Route path="/queue" element={<QueuePage />} />
            <Route path="/account" element={<AccountPage />} />
            {admin && <Route path="/users" element={<UsersPage />} />}
            {admin && (
              <Route path="/settings" element={<ApplicationSettingsPage />} />
            )}
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
          <footer className="page-footer">
            <span>OctoBackup / Database operations</span>
            <span>Self-hosted. Operator controlled.</span>
          </footer>
        </main>
      </div>
    </div>
  );
}
function ShieldMark() {
  return <Database size={21} aria-hidden="true" />;
}
