import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Pencil, Trash2, CalendarClock } from "lucide-react";
import { api } from "../../shared/api";
import type { Schedule } from "../../shared/types";
import { days } from "../../shared/types";
import {
  Dialog,
  ConfirmDialog,
  Empty,
  ErrorMessage,
  Loading,
  PageHeading,
  Status,
  Field,
  SubmitForm,
} from "../../shared/ui";
export function SchedulesPage() {
  const client = useQueryClient();
  const [edit, setEdit] = useState<Schedule | "new" | null>(null);
  const [removing, setRemoving] = useState<string | null>(null);
  const query = useQuery({
    queryKey: ["schedules"],
    queryFn: ({ signal }) =>
      api<Schedule[]>("schedules/GetMySchedules", undefined, signal),
  });
  return (
    <>
      <PageHeading
        title="Schedules"
        description="Make backup routines you can rely on. Times use the backend server’s timezone."
        action={
          <button onClick={() => setEdit("new")}>
            <Plus size={17} />
            Create schedule
          </button>
        }
      />
      {edit && (
        <Dialog
          title={
            edit === "new" ? "Create a backup schedule" : `Edit ${edit.name}`
          }
          onClose={() => setEdit(null)}
        >
          <ScheduleForm
            key={typeof edit === "string" ? edit : edit.id}
            schedule={edit === "new" ? undefined : edit}
            onCancel={() => setEdit(null)}
            onSaved={async () => {
              setEdit(null);
              await client.invalidateQueries();
            }}
          />
        </Dialog>
      )}
      {removing && (
        <ConfirmDialog
          title="Delete schedule"
          label="Confirm delete"
          onClose={() => setRemoving(null)}
          onConfirm={async () => {
            await api("schedules/DeleteSchedule", removing);
            await client.invalidateQueries();
          }}
        >
          <p>
            Delete{" "}
            <strong>{query.data?.find((s) => s.id === removing)?.name}</strong>?
            Future occurrences will stop. Existing backups remain available.
          </p>
        </ConfirmDialog>
      )}
      {query.isPending ? (
        <Loading />
      ) : query.isError ? (
        <ErrorMessage error={query.error} />
      ) : !query.data.length ? (
        <section className="panel">
          <Empty title="Put a good plan on repeat">
            Create a schedule for an accessible server. The worker checks due
            backups at the interval configured by your administrator.
          </Empty>
        </section>
      ) : (
        <section className="panel">
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">Schedule</th>
                  <th scope="col">Server</th>
                  <th scope="col">Runs on</th>
                  <th scope="col">Status</th>
                  <th scope="col">Actions</th>
                </tr>
              </thead>
              <tbody>
                {query.data.map((s) => (
                  <tr key={s.id}>
                    <td>
                      <strong className="table-title">
                        <CalendarClock size={18} />
                        {s.name}
                      </strong>
                    </td>
                    <td>{s.serverName}</td>
                    <td>
                      {s.selectedDays
                        .map((d) => days[d].slice(0, 3))
                        .join(", ")}
                      <small className="block">
                        {s.selectedTimes.map((t) => t.slice(0, 5)).join(", ")}
                      </small>
                    </td>
                    <td>
                      <Status muted={!s.isEnabled}>
                        {s.isEnabled ? "Enabled" : "Paused"}
                      </Status>
                    </td>
                    <td>
                      <div className="inline-actions">
                        <button
                          className="icon-button"
                          aria-label={`Edit ${s.name}`}
                          onClick={() => setEdit(s)}
                        >
                          <Pencil size={16} />
                        </button>
                        <button
                          className="icon-button danger-text"
                          aria-label={`Delete ${s.name}`}
                          onClick={() => setRemoving(s.id)}
                        >
                          <Trash2 size={16} />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}
    </>
  );
}
function ScheduleForm({
  schedule,
  onSaved,
  onCancel,
}: {
  schedule?: Schedule;
  onSaved: () => Promise<unknown>;
  onCancel: () => void;
}) {
  const [selected, setSelected] = useState(
    schedule?.selectedDays ?? [1, 2, 3, 4, 5],
  );
  const servers = useQuery({
    queryKey: ["schedule-servers"],
    queryFn: () =>
      api<{ id: string; name: string }[]>("servers/GetMyServersForSchedule"),
  });
  return (
    <>
      <ErrorMessage error={servers.error} />
      <SubmitForm
        onCancel={onCancel}
        label={schedule ? "Save schedule" : "Create schedule"}
        onSubmit={async (d) => {
          if (!selected.length) throw new Error("Select at least one day.");
          const times = String(d.get("times"))
            .split(",")
            .map((t) => t.trim());
          if (
            !times.length ||
            times.length > 24 ||
            times.some((t) => !/^([01]\d|2[0-3]):[0-5]\d$/.test(t))
          )
            throw new Error(
              "Enter up to 24 times in HH:MM format, separated by commas.",
            );
          await api(
            `schedules/${schedule ? "EditSchedule" : "CreateSchedule"}`,
            {
              id: schedule?.id ?? crypto.randomUUID(),
              name: d.get("name"),
              dbConnectionId: d.get("server"),
              isEnabled: d.has("enabled"),
              selectedDays: selected,
              selectedTimes: [...new Set(times)].map((t) => `${t}:00`),
            },
          );
          await onSaved();
        }}
      >
        <div className="form-grid">
          <Field label="Schedule name">
            <input
              name="name"
              defaultValue={schedule?.name}
              required
              maxLength={120}
              placeholder="Weekday backup"
            />
          </Field>
          <Field label="Server">
            <select
              name="server"
              defaultValue={schedule?.dbConnectionId ?? ""}
              required
            >
              <option value="" disabled>
                Select a server
              </option>
              {servers.data?.map((s) => (
                <option value={s.id} key={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          </Field>
        </div>
        <fieldset className="day-field">
          <legend>Days of the week</legend>
          <div className="day-picker">
            {[1, 2, 3, 4, 5, 6, 0].map((d) => (
              <label key={d} className={selected.includes(d) ? "selected" : ""}>
                <input
                  type="checkbox"
                  checked={selected.includes(d)}
                  onChange={(e) =>
                    setSelected(
                      e.target.checked
                        ? [...selected, d]
                        : selected.filter((v) => v !== d),
                    )
                  }
                />
                {days[d].slice(0, 3)}
              </label>
            ))}
          </div>
        </fieldset>
        <Field label="Run times (24-hour clock, separated by commas)">
          <input
            name="times"
            defaultValue={
              schedule?.selectedTimes.map((t) => t.slice(0, 5)).join(", ") ??
              "02:00"
            }
            required
            placeholder="02:00, 14:30"
          />
        </Field>
        <label className="check">
          <input
            name="enabled"
            type="checkbox"
            defaultChecked={schedule?.isEnabled ?? true}
          />
          Enable this schedule
        </label>
        <p className="hint">
          Execution is picked up at the polling interval configured by your
          administrator.
        </p>
      </SubmitForm>
    </>
  );
}
