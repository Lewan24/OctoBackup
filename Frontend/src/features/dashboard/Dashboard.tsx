import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import {
  ArrowRight,
  Plus,
  Server as ServerIcon,
  Archive,
  CalendarClock,
  ArrowUpRight,
  RefreshCw,
  Download,
} from "lucide-react";
import { api } from "../../shared/api";
import type { Backup, Server, Schedule } from "../../shared/types";
import { date, days, engineCodes, engines } from "../../shared/types";
import {
  Empty,
  ErrorMessage,
  Loading,
  PageHeading,
  Status,
} from "../../shared/ui";

export function Dashboard({ admin }: { admin: boolean }) {
  const query = useQuery({
    queryKey: ["overview"],
    queryFn: async ({ signal }) => {
      const [servers, backups, schedules] = await Promise.all([
        api<Server[]>("servers/GetMyServers", undefined, signal),
        api<Backup[]>("backups/GetAllBackups", undefined, signal),
        api<Schedule[]>("schedules/GetMySchedules", undefined, signal),
      ]);
      return { servers, backups, schedules };
    },
  });
  if (query.isPending) return <Loading />;
  if (query.isError)
    return (
      <>
        <ErrorMessage error={query.error} />
        <button onClick={() => void query.refetch()}>Try again</button>
      </>
    );
  const { servers, backups, schedules } = query.data;
  const recent = [...backups]
    .sort(
      (a, b) =>
        new Date(b.createdOn).getTime() - new Date(a.createdOn).getTime(),
    )
    .slice(0, 5);
  const active = servers.filter((s) => !s.isDisabled).length;
  const enabledSchedules = schedules.filter((s) => s.isEnabled);
  return (
    <>
      <PageHeading
        title="Backup overview"
        description="Your database fleet. Every copy accounted for."
        action={
          <div className="heading-actions">
            <button
              className="icon-button refresh-button"
              aria-label="Refresh overview"
              disabled={query.isFetching}
              onClick={() => void query.refetch()}
            >
              <RefreshCw size={16} className={query.isFetching ? "spin" : ""} />
            </button>
            <Link className="button" to="/servers">
              <Plus size={16} />
              {admin ? "Connect a server" : "View servers"}
            </Link>
          </div>
        }
      />
      <section className="metric-strip" aria-label="Workspace statistics">
        {[
          {
            label: "Connected servers",
            value: servers.length,
            icon: ServerIcon,
            detail: `${active} enabled`,
          },
          {
            label: "Backup records",
            value: backups.length,
            icon: Archive,
            detail: `${backups.filter((b) => !b.isDisabled).length} available`,
          },
          {
            label: "Active schedules",
            value: enabledSchedules.length,
            icon: CalendarClock,
            detail: `${schedules.length} configured`,
          },
        ].map(({ label, value, icon: Icon, detail }) => (
          <div className="metric" key={label}>
            <span className="metric-icon">
              <Icon size={18} />
            </span>
            <div>
              <span>{label}</span>
              <strong>{value}</strong>
            </div>
            <small>{detail}</small>
          </div>
        ))}
      </section>
      <div className="dashboard-grid">
        <section className="panel recent-panel">
          <div className="section-heading">
            <div>
              <h2>Recent backups</h2>
              <p>Latest records across your connections.</p>
            </div>
            <Link to="/backups" className="text-link">
              View history
              <ArrowUpRight size={15} />
            </Link>
          </div>
          {recent.length ? (
            <div className="table-scroll">
              <table>
                <thead>
                  <tr>
                    <th scope="col">Database</th>
                    <th scope="col">Created</th>
                    <th scope="col">File</th>
                  </tr>
                </thead>
                <tbody>
                  {recent.map((b) => (
                    <tr key={b.id}>
                      <td>
                        <strong>{b.name}</strong>
                      </td>
                      <td className="data-value">{date(b.createdOn)}</td>
                      <td>
                        <Status muted={b.isDisabled}>
                          {b.isDisabled ? "Expired" : "Available"}
                        </Status>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <Empty title="No backups yet">
              Run a backup from Servers. Your records will appear here.
            </Empty>
          )}
          <Link className="panel-link" to="/backups">
            <Download size={15} />
            Browse downloadable copies
            <ArrowRight size={15} />
          </Link>
        </section>
        <section className="panel">
          <div className="section-heading">
            <div>
              <h2>Connections</h2>
              <p>Configured database access.</p>
            </div>
            <Link
              to="/servers"
              aria-label="View all servers"
              className="icon-link"
            >
              <ArrowUpRight size={18} />
            </Link>
          </div>
          {servers.length ? (
            <div className="connection-list">
              {servers.slice(0, 4).map((s) => (
                <Link to="/servers" key={s.id}>
                  <span className={`engine-icon engine-${s.dbType}`}>
                    {engineCodes[s.dbType]}
                  </span>
                  <span>
                    <strong>{s.connectionName}</strong>
                    <small>
                      {engines[s.dbType]} / {s.dbName}
                    </small>
                  </span>
                  <Status muted={s.isDisabled}>
                    {s.isDisabled ? "Disabled" : "Enabled"}
                  </Status>
                </Link>
              ))}
            </div>
          ) : (
            <Empty title="No connections yet">
              {admin
                ? "Connect your first database to get started."
                : "Ask your administrator for server access."}
            </Empty>
          )}
          <Link className="panel-link" to="/servers">
            <ServerIcon size={15} />
            Manage connections
            <ArrowRight size={15} />
          </Link>
        </section>
      </div>
      <section className="panel schedule-summary">
        <div className="section-heading">
          <div>
            <h2>Scheduled operations</h2>
            <p>Enabled schedules use the server's local time.</p>
          </div>
          <Link to="/schedules" className="text-link">
            Manage schedules
            <ArrowUpRight size={15} />
          </Link>
        </div>
        {enabledSchedules.length ? (
          <div className="schedule-list">
            {enabledSchedules.slice(0, 3).map((schedule) => (
              <Link to="/schedules" key={schedule.id}>
                <CalendarClock size={17} />
                <span>
                  <strong>{schedule.name}</strong>
                  <small>
                    {schedule.serverName ??
                      servers.find((s) => s.id === schedule.dbConnectionId)
                        ?.connectionName ??
                      "Assigned server"}
                  </small>
                </span>
                <span className="schedule-timing">
                  {schedule.selectedDays
                    .map((d) => days[d]?.slice(0, 3))
                    .join(", ") || "No days selected"}
                  <small>
                    {schedule.selectedTimes
                      .map((time) => time.slice(0, 5))
                      .join(" / ") || "No times selected"}
                  </small>
                </span>
                <ArrowUpRight size={15} />
              </Link>
            ))}
          </div>
        ) : (
          <div className="schedule-empty">
            <CalendarClock size={19} />
            <p>
              No enabled schedules. Automate a backup to keep the next copy
              planned.
            </p>
            <Link className="button secondary small" to="/schedules">
              Create schedule
              <Plus size={14} />
            </Link>
          </div>
        )}
      </section>
    </>
  );
}
