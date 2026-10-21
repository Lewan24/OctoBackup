import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Download, Search, Archive } from "lucide-react";
import { api } from "../../shared/api";
import type { Backup } from "../../shared/types";
import { date } from "../../shared/types";
import {
  Empty,
  ErrorMessage,
  Loading,
  PageHeading,
  Status,
} from "../../shared/ui";
export function BackupsPage() {
  const [search, setSearch] = useState("");
  const [available, setAvailable] = useState(false);
  const [page, setPage] = useState(0);
  const query = useQuery({
    queryKey: ["backups"],
    queryFn: ({ signal }) =>
      api<Backup[]>("backups/GetAllBackups", undefined, signal),
  });
  const rows = [...(query.data ?? [])]
    .filter(
      (b) =>
        b.name.toLowerCase().includes(search.toLowerCase()) &&
        (!available || !b.isDisabled),
    )
    .sort(
      (a, b) =>
        new Date(b.createdOn).getTime() - new Date(a.createdOn).getTime(),
    );
  const pages = Math.max(1, Math.ceil(rows.length / 25));
  const current = Math.min(page, pages - 1);
  return (
    <>
      <PageHeading
        title="Backup history"
        description="Find a copy, see when it was created, and download it securely."
      />
      <div className="toolbar">
        <label className="search">
          <Search size={18} />
          <input
            aria-label="Search backups"
            placeholder="Search backup records…"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(0);
            }}
          />
        </label>
        <label className="check">
          <input
            type="checkbox"
            checked={available}
            onChange={(e) => {
              setAvailable(e.target.checked);
              setPage(0);
            }}
          />
          Available only
        </label>
      </div>
      {query.isPending ? (
        <Loading />
      ) : query.isError ? (
        <ErrorMessage error={query.error} />
      ) : (
        <section className="panel">
          {rows.length ? (
            <>
              <div className="table-scroll">
                <table>
                  <thead>
                    <tr>
                      <th scope="col">Backup</th>
                      <th scope="col">Created</th>
                      <th scope="col">Availability</th>
                      <th scope="col">Restore verification</th>
                      <th scope="col">Download</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.slice(current * 25, (current + 1) * 25).map((b) => (
                      <tr key={b.id}>
                        <td>
                          <strong className="table-title">
                            <Archive size={18} />
                            {b.name}
                          </strong>
                        </td>
                        <td>{date(b.createdOn)}</td>
                        <td>
                          <Status muted={b.isDisabled}>
                            {b.isDisabled ? "Expired" : "Available"}
                          </Status>
                        </td>
                        <td>
                          {b.test ? (
                            `Recorded result: ${b.test.status}`
                          ) : (
                            <span className="subtle">Not verified</span>
                          )}
                        </td>
                        <td>
                          {!b.isDisabled ? (
                            <a
                              className="button secondary small"
                              href={`/api/backups/Download?id=${encodeURIComponent(b.id)}`}
                              download
                            >
                              <Download size={15} />
                              Download
                            </a>
                          ) : (
                            <span className="subtle">Unavailable</span>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="pagination">
                <span>
                  {rows.length} records · Page {current + 1} of {pages}
                </span>
                <div>
                  <button
                    className="secondary"
                    disabled={current === 0}
                    onClick={() => setPage(current - 1)}
                  >
                    Previous
                  </button>
                  <button
                    className="secondary"
                    disabled={current + 1 >= pages}
                    onClick={() => setPage(current + 1)}
                  >
                    Next
                  </button>
                </div>
              </div>
            </>
          ) : (
            <Empty title="No backup records found">
              {search || available
                ? "Adjust your filters to see more results."
                : "Run a backup from the Servers page to create your first record."}
            </Empty>
          )}
        </section>
      )}
      <p className="hint">
        An available file has not necessarily been restore-tested. Automated
        restore verification is not yet implemented.
      </p>
    </>
  );
}
