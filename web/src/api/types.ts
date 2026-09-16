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
  | "ServiceFailed";
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
