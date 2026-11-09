import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Pencil } from "lucide-react";
import { api } from "../../shared/api";
import type { User } from "../../shared/types";
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
export function UsersPage() {
  const client = useQueryClient();
  const [edit, setEdit] = useState<User | "new" | null>(null);
  const [confirmation, setConfirmation] = useState<{
    user: User;
    path: string;
    label: string;
  } | null>(null);
  const query = useQuery({
    queryKey: ["users"],
    queryFn: () => api<User[]>("administration/GetUsers"),
  });
  return (
    <>
      <PageHeading
        title="Users & access"
        description="Invite operators and manage account access. Assign servers from the Servers page."
        action={
          <button onClick={() => setEdit("new")}>
            <Plus size={17} />
            Create user
          </button>
        }
      />
      {confirmation && (
        <ConfirmDialog
          title={confirmation.label}
          label={confirmation.label}
          onClose={() => setConfirmation(null)}
          onConfirm={async () => {
            await api(confirmation.path, confirmation.user.id);
            await client.invalidateQueries();
          }}
        >
          <p>
            <strong>{confirmation.user.email}</strong>
          </p>
          <p>
            {confirmation.path.includes("ConfirmUser")
              ? "Confirm this account so the operator can sign in. Server assignments are managed separately."
              : confirmation.user.isBlocked
                ? "Allow this operator to sign in again."
                : "Block this operator from signing in and using assigned resources."}
          </p>
        </ConfirmDialog>
      )}
      {edit && (
        <Dialog
          title={
            edit === "new" ? "Create an operator account" : `Edit ${edit.email}`
          }
          onClose={() => setEdit(null)}
        >
          <SubmitForm
            onCancel={() => setEdit(null)}
            label={edit === "new" ? "Create user" : "Save user"}
            onSubmit={async (d) => {
              if (d.get("password") !== d.get("confirm"))
                throw new Error("Passwords do not match.");
              await api(
                edit === "new" ? "auth/Register" : "administration/EditUser",
                edit === "new"
                  ? {
                      email: d.get("email"),
                      password: d.get("password"),
                      passwordConfirm: d.get("confirm"),
                    }
                  : {
                      id: edit.id,
                      email: d.get("email"),
                      password: d.get("password") || null,
                      confirmPassword: d.get("confirm") || null,
                    },
              );
              setEdit(null);
              await client.invalidateQueries();
            }}
          >
            <div className="form-grid">
              <Field label="Email address">
                <input
                  name="email"
                  type="email"
                  defaultValue={edit === "new" ? "" : edit.email}
                  required
                  maxLength={100}
                />
              </Field>
              <Field
                label={edit === "new" ? "Password" : "New password (optional)"}
              >
                <input
                  name="password"
                  type="password"
                  autoComplete="new-password"
                  minLength={12}
                  maxLength={128}
                  required={edit === "new"}
                />
              </Field>
              <Field label="Confirm password">
                <input
                  name="confirm"
                  type="password"
                  autoComplete="new-password"
                  minLength={12}
                  required={edit === "new"}
                />
              </Field>
            </div>
            <p className="hint">
              Passwords require 12 characters, mixed case, a number, and a
              symbol. New operator accounts are confirmed by the administrator
              creating them.
            </p>
          </SubmitForm>
        </Dialog>
      )}
      {query.isPending ? (
        <Loading />
      ) : query.isError ? (
        <ErrorMessage error={query.error} />
      ) : !query.data.length ? (
        <Empty title="No users found" />
      ) : (
        <section className="panel">
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">Email</th>
                  <th scope="col">Roles</th>
                  <th scope="col">Account</th>
                  <th scope="col">Actions</th>
                </tr>
              </thead>
              <tbody>
                {query.data.map((u) => (
                  <tr key={u.id}>
                    <td>
                      <strong>{u.email}</strong>
                    </td>
                    <td>{u.roles.join(", ")}</td>
                    <td>
                      <Status muted={u.isBlocked || !u.isEmailConfirmed}>
                        {u.isBlocked
                          ? "Blocked"
                          : u.isEmailConfirmed
                            ? "Confirmed"
                            : "Unconfirmed"}
                      </Status>
                    </td>
                    <td>
                      <div className="inline-actions">
                        <button
                          className="icon-button"
                          aria-label={`Edit ${u.email}`}
                          onClick={() => setEdit(u)}
                        >
                          <Pencil size={16} />
                        </button>
                        {!u.roles.includes("Admin") && (
                          <button
                            className="secondary small"
                            onClick={() =>
                              setConfirmation({
                                user: u,
                                path: "administration/ToggleUserBlockade",
                                label: u.isBlocked
                                  ? "Unblock user"
                                  : "Block user",
                              })
                            }
                          >
                            {u.isBlocked ? "Unblock" : "Block"}
                          </button>
                        )}
                        {!u.isEmailConfirmed && (
                          <button
                            className="secondary small"
                            onClick={() =>
                              setConfirmation({
                                user: u,
                                path: "administration/ConfirmUser",
                                label: "Confirm account",
                              })
                            }
                          >
                            Confirm account
                          </button>
                        )}
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
