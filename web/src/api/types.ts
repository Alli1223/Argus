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
  operator: AlertOperator;
  threshold: number;
  severity: AlertSeverity;
  status: AlertStatus;
  value: number | null;
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
