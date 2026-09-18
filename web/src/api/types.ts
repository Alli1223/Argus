// Mirrors of the server's JSON contracts (camelCase properties, enums as strings).

export type HostPlatform = "Unknown" | "Linux" | "Windows" | "MacOS";
export type HostStatus = "Online" | "Offline";

export type AlertMetric =
  | "CpuUsage"
  | "MemoryUsage"
  | "SwapUsage"
  | "LoadPerCore"
  | "DiskIoUtilization"
  | "NetworkReceive"
  | "NetworkTransmit"
  | "DiskUsage"
  | "InodeUsage"
  | "HostOffline"
  | "ServiceFailed"
  | "ContainerDown"
  | "ContainerRestarts";
export type AlertOperator = "Above" | "Below";
/** A fixed threshold, or the host's usual level (the threshold then counts standard deviations). */
export type AlertCondition = "Threshold" | "Anomaly";
export type AlertSeverity = "Info" | "Warning" | "Critical";
export type AlertStatus = "Firing" | "Resolved";

export interface ServerInfo {
  name: string;
  version: string;
  /** Address agents and people use to reach the server, when configured. */
  publicUrl: string | null;
}

export interface AuthStatus {
  setupRequired: boolean;
  registrationEnabled: boolean;
}

export interface CurrentUser {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
  isAdmin: boolean;
}

export interface NewAccount {
  email: string;
  password: string;
  displayName: string;
}

export interface Credentials {
  email: string;
  password: string;
  rememberMe: boolean;
}

export interface LatestMetrics {
  time: string;
  cpuPercent: number;
  memoryPercent: number;
  memoryUsedBytes: number;
  memoryTotalBytes: number;
  swapPercent: number | null;
  load1: number | null;
  diskUsedPercent: number | null;
  netRxBytesPerSec: number | null;
  netTxBytesPerSec: number | null;
  uptimeSeconds: number;
}

export interface HostSummary {
  id: string;
  displayName: string;
  hostname: string;
  platform: HostPlatform;
  osName: string | null;
  tags: string[];
  status: HostStatus;
  lastSeenAt: string | null;
  agentVersion: string;
  ownerId: string;
  latest: LatestMetrics | null;
  agentUpdate: HostAgentUpdate | null;
}

export interface HostDetail {
  id: string;
  displayName: string;
  hostname: string;
  platform: HostPlatform;
  osName: string | null;
  osVersion: string | null;
  kernelVersion: string | null;
  architecture: string;
  cpuModel: string | null;
  cpuCores: number | null;
  cpuLogicalProcessors: number;
  memoryTotalBytes: number;
  bootTime: string | null;
  ipAddresses: string[];
  agentVersion: string;
  tags: string[];
  notes: string | null;
  status: HostStatus;
  createdAt: string;
  lastSeenAt: string | null;
  inventoryUpdatedAt: string | null;
  ownerId: string;
  latest: LatestMetrics | null;
  agentUpdate: HostAgentUpdate | null;
}

/**
 * A host's agent and updates: a newer release it could install, the version someone asked it to
 * install and when, and why the last attempt failed.
 */
export interface HostAgentUpdate {
  available: string | null;
  requested: string | null;
  requestedAt: string | null;
  error: string | null;
}

export interface ReleaseSummary {
  version: string;
  tag: string;
  name: string;
  /** The release notes, in Markdown. */
  notes: string;
  url: string;
  publishedAt: string;
}

/** This server's version against the latest release. */
export interface ServerUpdateInfo {
  currentVersion: string;
  enabled: boolean;
  latest: ReleaseSummary | null;
  updateAvailable: boolean;
  checkedAt: string | null;
  error: string | null;
}

/** Where an update of the server stands: steps in order, then how it ended. */
export type ServerUpdateState =
  | "starting"
  | "downloading"
  | "backing-up"
  | "deploying"
  | "verifying"
  | "rolling-back"
  | "succeeded"
  | "rolled-back"
  | "failed";

/** One update of the server, as the updater service records it. */
export interface ServerUpdateRun {
  id: string;
  from: string;
  to: string;
  requestedBy: string | null;
  state: ServerUpdateState;
  inProgress: boolean;
  startedAt: string;
  finishedAt: string | null;
  error: string | null;
  /** Where the backup taken before the update is, on the server's machine. */
  backup: string | null;
  /** The new server's last log lines, when it had to be rolled back. */
  serverLog: string | null;
  log: { at: string; message: string }[] | null;
}

/** Whether Argus can install releases itself (or why not), and its latest update. */
export interface ServerSelfUpdate {
  available: boolean;
  unavailable: string | null;
  updaterVersion: string | null;
  /** A version asked for that the updater has not started on yet. */
  pendingVersion: string | null;
  lastRun: ServerUpdateRun | null;
}

export interface UpdateHost {
  displayName?: string;
  tags?: string[];
  notes?: string;
}

/** Columnar series: `time` holds unix seconds, each series one value (or a gap) per timestamp. */
export interface MetricSeries {
  from: string;
  to: string;
  resolution: "raw" | "5m" | "1h";
  bucketSeconds: number;
  time: number[];
  series: Record<string, (number | null)[]>;
}

/** One host's temperature sensors over time; series are keyed `{device}/{sensor}`. */
export interface HostTemperatures {
  hostId: string;
  displayName: string;
  history: MetricSeries;
}

/** What a running container used in its newest sample. */
export interface ContainerUsageSnapshot {
  time: string;
  /** Share of the whole machine's CPU, 0–100. */
  cpuPercent: number;
  memoryBytes: number;
  memoryLimitBytes: number | null;
  /** Null for containers without a network of their own, such as those on the host's. */
  netRxBytesPerSec: number | null;
  netTxBytesPerSec: number | null;
}

export type ContainerState =
  "created" | "running" | "paused" | "restarting" | "removing" | "exited" | "dead" | "unknown";

export interface ContainerSummary {
  name: string;
  id: string;
  image: string;
  state: ContainerState;
  health: "starting" | "healthy" | "unhealthy" | null;
  restartCount: number;
  /** How often Docker restarted it in the past hour. */
  restartsLastHour: number;
  exitCode: number | null;
  oomKilled: boolean;
  createdAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  /** When its state or health last changed, as far as Argus saw. */
  stateSince: string;
  restartPolicy: string | null;
  composeProject: string | null;
  composeService: string | null;
  ports: string[];
  usage: ContainerUsageSnapshot | null;
}

/** A host's containers; `checkedAt` is null when its agent has never reported any. */
export interface HostContainers {
  checkedAt: string | null;
  engineVersion: string | null;
  /** Why the agent could not read Docker. */
  problem: string | null;
  actionsEnabled: boolean;
  containers: ContainerSummary[];
}

export type ContainerEventKind =
  | "appeared"
  | "removed"
  | "recreated"
  | "started"
  | "stopped"
  | "restarting"
  | "restarted"
  | "paused"
  | "died"
  | "unhealthy"
  | "healthy"
  | "start-requested"
  | "stop-requested"
  | "restart-requested";

export interface ContainerEventInfo {
  time: string;
  kind: ContainerEventKind;
  detail: string | null;
  count: number;
}

export interface ContainerLogLine {
  time: string | null;
  stream: "stdout" | "stderr";
  text: string;
}

/** A container's newest log lines, oldest first; `truncated` when older ones were left out. */
export interface ContainerLogs {
  lines: ContainerLogLine[];
  truncated: boolean;
}

export interface ContainerDetail {
  hostId: string;
  hostName: string;
  actionsEnabled: boolean;
  container: ContainerSummary;
  events: ContainerEventInfo[];
}

export interface ContainerHost {
  hostId: string;
  hostName: string;
  checkedAt: string;
  problem: string | null;
  actionsEnabled: boolean;
  containers: ContainerSummary[];
}

export interface FilesystemSnapshot {
  mountPoint: string;
  device: string | null;
  fsType: string | null;
  totalBytes: number;
  usedBytes: number;
  availableBytes: number;
  usedPercent: number;
  inodesTotal: number | null;
  inodesUsed: number | null;
  time: string;
}

export interface ProcessMetrics {
  pid: number;
  name: string;
  cpuPercent: number;
  memoryBytes: number;
}

export interface ProcessSnapshot {
  capturedAt: string;
  processes: ProcessMetrics[];
}

export interface ServiceFailure {
  service: string;
  description: string | null;
  state: string;
  /** When the failure was first seen; it keeps this time until the service recovers. */
  since: string;
}

/** A host's services: when its agent last checked (null if never) and what was failing then. */
export interface ServiceStatus {
  checkedAt: string | null;
  failures: ServiceFailure[];
}

export interface EnrollmentTokenSummary {
  id: string;
  name: string;
  tokenPrefix: string;
  tags: string[];
  createdAt: string;
  expiresAt: string | null;
  maxUses: number | null;
  useCount: number;
  lastUsedAt: string | null;
  revokedAt: string | null;
  isActive: boolean;
}

export interface CreatedEnrollmentToken {
  token: string;
  summary: EnrollmentTokenSummary;
}

export interface CreateEnrollmentToken {
  name: string;
  expiresInHours?: number | null;
  maxUses?: number | null;
  tags: string[];
}

export interface UserSummary {
  id: string;
  email: string;
  displayName: string;
  role: "Admin" | "User";
  createdAt: string;
  lastLoginAt: string | null;
  isDisabled: boolean;
  isLockedOut: boolean;
}

export interface AlertRule {
  id: string;
  name: string;
  metric: AlertMetric;
  condition: AlertCondition;
  operator: AlertOperator;
  threshold: number;
  durationSeconds: number;
  severity: AlertSeverity;
  hostId: string | null;
  hostName: string | null;
  tag: string | null;
  resourceFilter: string | null;
  enabled: boolean;
  firingAlerts: number;
  createdAt: string;
  updatedAt: string;
}

export interface AlertRuleRequest {
  name: string;
  metric: AlertMetric;
  condition: AlertCondition;
  operator: AlertOperator;
  threshold: number;
  durationSeconds: number;
  severity: AlertSeverity;
  hostId: string | null;
  tag: string | null;
  resourceFilter: string | null;
  enabled: boolean;
}

export interface Alert {
  id: string;
  ruleId: string | null;
  ruleName: string | null;
  hostId: string;
  hostName: string;
  resourceKey: string;
  title: string;
  metric: AlertMetric;
  condition: AlertCondition;
  operator: AlertOperator;
  threshold: number;
  severity: AlertSeverity;
  status: AlertStatus;
  value: number | null;
  /** For anomaly alerts: the host's usual level, which the value strayed from. */
  baseline: number | null;
  firedAt: string;
  resolvedAt: string | null;
  acknowledgedAt: string | null;
  acknowledgedBy: string | null;
}

export interface AlertPage {
  items: Alert[];
  total: number;
  page: number;
  pageSize: number;
}

export interface AlertCounts {
  critical: number;
  warning: number;
  info: number;
  total: number;
}

export interface DashboardSummary {
  totalHosts: number;
  onlineHosts: number;
  offlineHosts: number;
  platforms: Record<string, number>;
  activeAlerts: AlertCounts;
  busiestByCpu: HostSummary[];
  fullestDisks: HostSummary[];
}

export type NotificationChannelKind = "Email" | "Webhook" | "Slack" | "Discord";
export type DeliveryStatus = "Pending" | "Sent" | "Failed";

/** A channel's most recent notification: when it was queued, whether it went out, and why not. */
export interface DeliverySummary {
  status: DeliveryStatus;
  attempts: number;
  createdAt: string;
  sentAt: string | null;
  lastError: string | null;
}

export interface NotificationChannel {
  id: string;
  name: string;
  kind: NotificationChannelKind;
  /** Email addresses separated by commas, or the webhook URL. */
  target: string;
  minimumSeverity: AlertSeverity;
  notifyOnResolved: boolean;
  enabled: boolean;
  dailyReport: boolean;
  weeklyReport: boolean;
  lastDelivery: DeliverySummary | null;
  createdAt: string;
  updatedAt: string;
}

export interface NotificationChannelRequest {
  name: string;
  kind: NotificationChannelKind;
  target: string;
  minimumSeverity: AlertSeverity;
  notifyOnResolved: boolean;
  enabled: boolean;
  dailyReport: boolean;
  weeklyReport: boolean;
}

/** What the server can send (email needs a mail server in its settings), and when reports go out. */
export interface NotificationSupport {
  email: boolean;
  reportHourUtc: number;
  weeklyReportDay: string;
}

/** How Argus connects to the mail server. `Auto` is TLS on port 465 and STARTTLS elsewhere. */
export type SmtpSecurity = "Auto" | "None" | "StartTls" | "SslOnConnect";

/** Where the mail server settings in use come from. */
export type EmailSettingsSource = "None" | "File" | "App";

/** The mail server Argus sends through. The password is never sent back, only `hasPassword`. */
export interface EmailSettings {
  configured: boolean;
  source: EmailSettingsSource;
  host: string | null;
  port: number;
  security: SmtpSecurity;
  username: string | null;
  hasPassword: boolean;
  from: string | null;
  fromName: string;
  updatedAt: string | null;
  updatedBy: string | null;
}

/** New mail server settings. Leave `password` out to keep the saved one; send "" to clear it. */
export interface EmailSettingsRequest {
  host: string;
  port: number;
  security: SmtpSecurity;
  username: string | null;
  password?: string | null;
  from: string;
  fromName: string;
}
