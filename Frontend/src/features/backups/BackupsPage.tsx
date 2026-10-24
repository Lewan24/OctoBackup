import { useId, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { restoreStatuses } from "./RestoreTestPage";
import { useQuery } from "@tanstack/react-query";
import {
  Download,
  Search,
  Archive,
  ChevronDown,
  ShieldCheck,
  ShieldX,
  Clock3,
  CircleHelp,
  FlaskConical,
  LockKeyhole,
  UnlockKeyhole,
} from "lucide-react";
import { api, downloadDecryptedBackup } from "../../shared/api";
import type { Backup } from "../../shared/types";
import { date } from "../../shared/types";
import {
  Dialog,
  Empty,
  ErrorMessage,
  Loading,
  PageHeading,
  Status,
  Field,
  SubmitForm,
} from "../../shared/ui";
export function BackupsPage() {
  const [decrypting, setDecrypting] = useState<Backup | null>(null);
  const [search, setSearch] = useState("");
  const [available, setAvailable] = useState(false);
  const [page, setPage] = useState(0);
  const query = useQuery({
    queryKey: ["backups"],
    queryFn: ({ signal }) =>
      api<Backup[]>("backups/GetAllBackups", undefined, signal),
    refetchInterval: 5000,
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
      {decrypting && (
        <Dialog
          title={`Download ${decrypting.name}`}
          onClose={() => setDecrypting(null)}
        >
          <p>
            Enter the encryption password used when this backup was created.
            Your account must also have access to this server.
          </p>
          <SubmitForm
            key={decrypting.id}
            label="Decrypt and download"
            onCancel={() => setDecrypting(null)}
            onSubmit={async (data) => {
              await downloadDecryptedBackup(
                decrypting.id,
                String(data.get("password")),
              );
              setDecrypting(null);
            }}
          >
            <Field label="Backup encryption password / key">
              <input
                name="password"
                type="password"
                autoComplete="off"
                required
                maxLength={512}
              />
            </Field>
          </SubmitForm>
        </Dialog>
      )}
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
                <table className="backup-history-table">
                  <thead>
                    <tr>
                      <th scope="col">Backup</th>
                      <th scope="col">Created</th>
                      <th scope="col">Availability</th>
                      <th scope="col">Restore verification</th>
                      <th scope="col">Actions</th>
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
                          <VerificationLabel backup={b} />
                        </td>
                        <td>
                          <div className="backup-actions">
                            <Link
                              className="button secondary small"
                              to={`/backups/${b.id}/restore-test`}
                            >
                              <FlaskConical size={18} />
                              Test / preview
                            </Link>
                            {!b.isDisabled ? (
                              <BackupDownloadMenu
                                backup={b}
                                onDecrypt={() => setDecrypting(b)}
                              />
                            ) : (
                              <span className="subtle">
                                Download unavailable
                              </span>
                            )}
                          </div>
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
        An available file has not necessarily been restore-tested. Tests restore
        into disposable containers and capture bounded data samples. Automatic
        tests can be enabled by an administrator.
      </p>
    </>
  );
}

function VerificationLabel({ backup }: { backup: Backup }) {
  const status = backup.test?.status;
  const label =
    status === undefined
      ? "Not tested"
      : (restoreStatuses[status] ?? "Unknown");
  const tone =
    status === 1
      ? "success"
      : status === 2
        ? "failed"
        : status === 0 || status === 3
          ? "pending"
          : "neutral";
  const Icon =
    status === 1
      ? ShieldCheck
      : status === 2
        ? ShieldX
        : status === 0 || status === 3
          ? Clock3
          : CircleHelp;
  return (
    <span className={`verification-label verification-${tone}`}>
      <Icon size={17} aria-hidden="true" />
      {label}
    </span>
  );
}

function BackupDownloadMenu({
  backup,
  onDecrypt,
}: {
  backup: Backup;
  onDecrypt: () => void;
}) {
  const id = useId();
  const menu = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  return (
    <>
      <button
        className="secondary small"
        type="button"
        popoverTarget={id}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={id}
        aria-label={`Download ${backup.name}`}
        onKeyDown={(event) => {
          if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault();
            if (!menu.current?.matches(":popover-open"))
              event.currentTarget.click();
            menu.current
              ?.querySelector<HTMLElement>('[role="menuitem"]')
              ?.focus();
          }
        }}
      >
        <Download size={18} />
        Download
        <ChevronDown size={16} />
      </button>
      <div
        id={id}
        ref={menu}
        popover="auto"
        role="menu"
        aria-label={`Download options for ${backup.name}`}
        className="backup-download-menu"
        onToggle={(event) => {
          setOpen(event.newState === "open");
          if (
            event.newState === "open" &&
            !menu.current?.contains(document.activeElement)
          )
            menu.current
              ?.querySelector<HTMLElement>('[role="menuitem"]')
              ?.focus();
        }}
        onKeyDown={(event) => {
          if (event.key === "Tab") {
            menu.current?.hidePopover();
            return;
          }
          const items = Array.from(
            menu.current?.querySelectorAll<HTMLElement>('[role="menuitem"]') ??
              [],
          );
          const index = items.indexOf(document.activeElement as HTMLElement);
          const next =
            event.key === "ArrowDown"
              ? (index + 1) % items.length
              : event.key === "ArrowUp"
                ? (index - 1 + items.length) % items.length
                : event.key === "Home"
                  ? 0
                  : event.key === "End"
                    ? items.length - 1
                    : null;
          if (next !== null) {
            event.preventDefault();
            items[next]?.focus();
          }
        }}
      >
        <a
          role="menuitem"
          href={`/api/backups/Download?id=${encodeURIComponent(backup.id)}`}
          download
          onClick={() => menu.current?.hidePopover()}
        >
          {backup.isEncrypted ? (
            <LockKeyhole size={18} aria-hidden="true" />
          ) : (
            <UnlockKeyhole size={18} aria-hidden="true" />
          )}
          {backup.isEncrypted ? "Download encrypted" : "Download unencrypted"}
        </a>
        {backup.isEncrypted && (
          <button
            role="menuitem"
            className="secondary"
            type="button"
            onClick={() => {
              menu.current?.hidePopover();
              onDecrypt();
            }}
          >
            <UnlockKeyhole size={18} aria-hidden="true" />
            Download decrypted
          </button>
        )}
      </div>
    </>
  );
}
