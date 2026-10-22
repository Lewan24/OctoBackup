import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../shared/api";
import {
  useRestoreAvailability,
  restoreRuntimeHelp,
} from "../../shared/restoreTesting";
import { date } from "../../shared/types";
import {
  Empty,
  ErrorMessage,
  Loading,
  PageHeading,
  Status,
} from "../../shared/ui";

export const restoreStatuses = [
  "Running",
  "Restore passed",
  "Failed",
  "Queued",
  "Interrupted",
  "Skipped",
];
export type RestoreReport = {
  id: string;
  backupId: string;
  status: number;
  automatic: boolean;
  testedOn: string;
  finishedOn?: string;
  message?: string;
  preview?: {
    tableCount: number;
    notes: string;
    catalog?: { schema: string; name: string }[];
    tables: {
      schema: string;
      name: string;
      columns: string[];
      rows: (string | null)[][];
    }[];
  };
};

export function RestoreTestPage() {
  const { id } = useParams();
  const client = useQueryClient();
  const availability = useRestoreAvailability();
  const [selected, setSelected] = useState(0);
  const key = ["restore-report", id];
  const report = useQuery({
    queryKey: key,
    queryFn: async ({ signal }) =>
      (await api<RestoreReport | null>(
        `backups/RestoreReport?id=${encodeURIComponent(id!)}`,
        undefined,
        signal,
      )) ?? null,
    refetchInterval: (query) =>
      query.state.data && [0, 3].includes(query.state.data.status)
        ? 2000
        : false,
  });
  const test = useMutation({
    mutationFn: () => api<RestoreReport>("backups/TestBackup", id),
    onSuccess: (result) => {
      client.setQueryData(key, result);
      setSelected(0);
      void client.invalidateQueries({ queryKey: ["backups"] });
    },
  });
  const result = report.data;
  const busy = result && [0, 3].includes(result.status);
  const preview = result?.preview;
  const table = preview?.tables[Math.min(selected, preview.tables.length - 1)];
  return (
    <>
      <PageHeading
        title="Restore test"
        description="Restore an archived backup in a disposable database and inspect the saved sample."
      />
      <div className="toolbar">
        <Link className="button secondary" to="/backups">
          Back to backup history
        </Link>
        <button
          disabled={
            report.isPending ||
            report.isError ||
            !!busy ||
            test.isPending ||
            availability.data?.enabled !== true
          }
          onClick={() => test.mutate()}
        >
          {test.isPending
            ? "Queuing test…"
            : busy
              ? "Test in progress"
              : "Test backup"}
        </button>
        <button
          className="secondary"
          onClick={() => void report.refetch()}
          disabled={report.isFetching}
        >
          Refresh report
        </button>
      </div>
      <ErrorMessage error={report.error || test.error} />
      <ErrorMessage error={availability.error} />
      {availability.data?.enabled === false && (
        <p className="notice" role="status">
          {restoreRuntimeHelp} Existing reports remain available.
        </p>
      )}
      {report.isPending ? (
        <Loading />
      ) : result ? (
        <section
          className="panel settings-editor"
          aria-label="Restore test report"
        >
          <h2>
            <Status muted={result.status !== 1}>
              {restoreStatuses[result.status] ?? "Unknown"}
            </Status>
          </h2>
          <p>
            {result.automatic ? "Automatic" : "Manual"} test requested{" "}
            {date(result.testedOn)}
            {result.finishedOn ? ` · Finished ${date(result.finishedOn)}` : ""}
          </p>
          {busy && (
            <p role="status">
              {result.status === 3
                ? "Waiting for the restore worker. One test runs at a time."
                : "Restoring the artifact and reading sample data. The container is removed when testing ends."}
            </p>
          )}
          {result.message && (
            <p className="notice" role="status">
              {result.message}
            </p>
          )}
          {preview && (
            <>
              <h2>
                {preview.tableCount} user{" "}
                {preview.tableCount === 1 ? "table" : "tables"} restored
              </h2>
              <p>{preview.notes}</p>
              {!!preview.catalog?.length && (
                <details>
                  <summary>Table inventory ({preview.catalog.length})</summary>
                  <ul>
                    {preview.catalog.map((t) => (
                      <li key={`${t.schema}.${t.name}`}>
                        {t.schema}.{t.name}
                      </li>
                    ))}
                  </ul>
                </details>
              )}
              <p className="hint">
                This is a saved preview from the restored container. Values may
                contain sensitive application data. NULL values and truncated
                cells are shown explicitly; row order is unspecified.
              </p>
              <label className="restore-table-select">
                Sampled table
                <select
                  value={Math.min(selected, preview.tables.length - 1)}
                  onChange={(e) => setSelected(Number(e.target.value))}
                >
                  {preview.tables.map((t, index) => (
                    <option key={`${t.schema}.${t.name}`} value={index}>
                      {t.schema}.{t.name} · {t.rows.length} sampled rows
                    </option>
                  ))}
                </select>
              </label>
              {table && (
                <>
                  <h3>
                    {table.schema}.{table.name}
                  </h3>
                  {table.rows.length ? (
                    <div className="table-scroll restore-data">
                      <table>
                        <caption className="hint">
                          Up to 20 rows; up to 64 columns. Text at 256
                          characters may be truncated.
                        </caption>
                        <thead>
                          <tr>
                            {table.columns.map((c) => (
                              <th scope="col" key={c}>
                                {c}
                              </th>
                            ))}
                          </tr>
                        </thead>
                        <tbody>
                          {table.rows.map((row, i) => (
                            <tr key={i}>
                              {row.map((cell, j) => (
                                <td key={j}>
                                  {cell === null ? (
                                    <span className="subtle">NULL</span>
                                  ) : cell === "" ? (
                                    <span className="subtle">
                                      (empty string)
                                    </span>
                                  ) : (
                                    cell
                                  )}
                                </td>
                              ))}
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  ) : (
                    <p className="notice">
                      This table is empty. Restore succeeded, but no data rows
                      were found in this sampled table.
                    </p>
                  )}
                </>
              )}
            </>
          )}
        </section>
      ) : (
        !report.isError && (
          <section className="panel">
            <Empty title="No restore test recorded">
              Select Test backup to queue a restore and generate a data preview.
            </Empty>
          </section>
        )
      )}
    </>
  );
}
