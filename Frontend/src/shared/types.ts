export type CurrentUser = {
  isAuthenticated: boolean;
  userName: string | null;
  claims: Record<string, string>;
};
export type Tunnel = {
  id: string;
  serverHost: string;
  sshPort: number;
  username: string;
  password?: string;
  privateKeyContent?: string;
  privateKeyPassphrase?: string;
  overridePasswordsAndPem?: boolean;
  localPort: number;
  remoteHost: string;
  remotePort: number;
  description?: string;
};
export type Server = {
  id: string;
  connectionName: string;
  serverHost: string;
  serverPort: number;
  dbName: string;
  dbType: number;
  dbUser: string;
  dbPasswd?: string;
  ssL: boolean;
  isDisabled: boolean;
  isTunnelRequired: boolean;
  tunnel?: Tunnel | null;
  autoTestBackupsConfigId?: string;
};
export type Schedule = {
  id: string;
  name: string;
  isEnabled: boolean;
  dbConnectionId: string;
  serverName?: string;
  selectedDays: number[];
  selectedTimes: string[];
};
export type Backup = {
  isEncrypted?: boolean;
  id: string;
  name: string;
  createdOn: string;
  serverConnectionId?: string;
  isDisabled: boolean;
  test?: { status: number; testedOn: string; errorMessage?: string };
};
export type User = {
  id: string;
  email: string;
  isBlocked: boolean;
  isEmailConfirmed: boolean;
  roles: string[];
};
export const engines = [
  "MySQL / MariaDB",
  "PostgreSQL",
  "SQL Server",
  "SQLite",
];
export const engineCodes = ["MY", "PG", "MS", "SQ"];
export const days = [
  "Sunday",
  "Monday",
  "Tuesday",
  "Wednesday",
  "Thursday",
  "Friday",
  "Saturday",
];
export function date(value: string) {
  return new Date(value).toLocaleString(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  });
}
