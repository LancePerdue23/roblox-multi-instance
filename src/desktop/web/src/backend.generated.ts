// Generated from src/desktop/backend/Contracts.cs. Do not edit.
export interface AccountBatchDto {
  batch: LaunchBatchResultDto;
  layoutStatus: string;
}
export interface AccountCreationProgressDto {
  completed: number;
  skipped: number;
  remaining: number;
  status: string;
  code: string;
}
export interface AccountDto {
  id: string;
  nickname: string;
  username: string;
  status: string;
  group: string;
  notes: string;
  color: string;
  favorite: boolean;
  order: number;
  robloxUserId: string | null;
  avatar: string | null;
  bypassOptimizations: boolean;
  badges: SpecialBadgeDto[] | null;
  hasStoredCredentials: boolean;
}
export interface AccountsStateDto {
  available: boolean;
  launchAvailable: boolean;
  notice: string;
  accounts: AccountDto[];
}
export interface ActivityDto {
  available: boolean;
  records: ActivityRowDto[];
}
export interface ActivityRowDto {
  id: string;
  startedUtc: string;
  endedUtc: string | null;
  durationMs: number | null;
  source: string;
  outcome: string;
  code: string;
  message: string;
}
export interface AppStateDto {
  version: string;
  helperVersion: string;
  helperInstalled: boolean;
  sharedNotice: string;
  readiness: string;
}
export interface CheckDto {
  id: string;
  name: string;
  state: string;
  code: string;
  message: string;
  action: string;
}
export interface GameCardDto {
  placeId: string;
  universeId: string | null;
  rootPlaceId: string | null;
  name: string;
  artwork: string | null;
  artworkState: string;
  artworkKind: string;
  accountIds: string[] | null;
  preset: string | null;
  arrangement: string | null;
}
export interface HealthDto {
  overall: string;
  helperVersion: string;
  browserIntegrationEnabled: boolean | null;
  checks: CheckDto[];
  readiness: string;
}
export interface InstanceDto {
  id: string;
  title: string;
  pid: number;
  username: string | null;
  cpuPercent: number | null;
  workingSet: number | null;
  started: string;
  hasWindow: boolean;
  bypassOptimizations: boolean;
  optimizationStatus: string;
}
export interface InstanceListDto {
  available: boolean;
  unavailable: number;
  instances: InstanceDto[];
  generation: number;
}
export interface LaunchBatchItemDto {
  accountId: string;
  slot: number;
  status: string;
  code: string;
  pid: number | null;
  created: number | null;
  plannedServer: string | null;
  observedServer: string | null;
  elapsedMs: number;
}
export interface LaunchBatchProgressDto {
  batchId: string;
  accountId: string;
  slot: number;
  status: string;
  code: string;
  elapsedMs: number;
  plannedServer: string | null;
}
export interface LaunchBatchResultDto {
  batchId: string;
  status: string;
  canApplyLayout: boolean;
  presetName: string | null;
  items: LaunchBatchItemDto[];
}
export interface LaunchTargetDto {
  placeId: string;
  jobId: string | null;
  game: GameCardDto | null;
}
export interface LayoutStateDto {
  monitors: MonitorDto[];
  presets: PresetDto[];
  shortcuts: Record<string, number>;
}
export interface MonitorDto {
  id: string;
  label: string;
  work: RectDto;
  dpi: number;
  primary: boolean;
}
export interface NotificationsDto {
  showProgress: boolean;
  showCompletion: boolean;
  available: boolean;
}
export interface OperationItemDto {
  id: string;
  outcome: string;
  code: string;
  message: string;
}
export interface OperationResultDto {
  outcome: string;
  code: string;
  message: string;
  items: OperationItemDto[];
}
export interface OptimizationInstanceDto {
  accountId: string;
  pid: number;
  startedAt: string;
  state: string;
  message: string;
  requested: string;
  effective: string;
  launchState: string;
  bypassOptimizations: boolean;
}
export interface OptimizationStateDto {
  enabled: boolean;
  applied: number;
  bypassed: number;
  unavailable: number;
  failed: number;
  instances: OptimizationInstanceDto[];
  launchCompatibility: string;
  available: boolean;
  error: string;
}
export interface PerformanceCapabilityDto {
  key: string;
  label: string;
  state: string;
  reason: string;
  applied: boolean;
  requested: string;
  effective: string;
  source: string;
}
export interface PerformanceControlsDto {
  id: string;
  available: boolean;
  ownsLease: boolean;
  recoveryRequired: boolean;
  priority: string | null;
  ecoQos: boolean | null;
  ownsPriority: boolean;
  ownsEcoQos: boolean;
  code: string;
  message: string;
}
export interface PerformanceExportDto {
  json: string;
}
export interface PerformanceProcessDto {
  id: string;
  pid: number;
  startedAt: string;
  cpuPercent: number | null;
  workingSetBytes: number | null;
  privateWorkingSetBytes: number | null;
  privateCommitBytes: number | null;
  readBytesPerSecond: number | null;
  writeBytesPerSecond: number | null;
  uptimeSeconds: number;
  status: string;
}
export interface PerformanceProfileDto {
  id: string;
  name: string;
  description: string;
  cpuPriority: string;
  fpsCap: number | null;
  graphicsQuality: number | null;
  ecoQos: string;
  textureQuality: string;
  minimumMsaa: boolean;
  disableGrass: boolean;
  reducedGeometry: boolean;
  graySky: boolean;
  windowBehavior: string;
}
export interface PerformanceProfilesDto {
  version: number;
  available: boolean;
  selectedProfileId: string;
  profiles: PerformanceProfileDto[];
  capabilities: PerformanceCapabilityDto[];
  revision: string;
  enabled: boolean;
  focusBoost: boolean;
}
export interface PerformanceRecoveryDto {
  succeeded: boolean;
  pending: number;
  message: string;
}
export interface PerformanceSnapshotDto {
  generation: number;
  observedAt: string;
  available: boolean;
  unavailableProcesses: number;
  processes: PerformanceProcessDto[];
  totals: PerformanceTotalsDto;
}
export interface PerformanceTotalsDto {
  processCount: number;
  cpuPercent: number | null;
  workingSetBytes: number | null;
  privateWorkingSetBytes: number | null;
  privateCommitBytes: number | null;
  readBytesPerSecond: number | null;
  writeBytesPerSecond: number | null;
}
export interface PlacementDto {
  id: string;
  x: number;
  y: number;
  width: number;
  height: number;
  slot: number;
}
export interface PlanDto {
  planId: string;
  valid: boolean;
  error: string | null;
  notes: string[];
  monitors: MonitorDto[];
  placements: PlacementDto[];
}
export interface PresetDto {
  name: string;
  slots: SlotDto[];
}
export interface PreviewSourceDto {
  id: string;
  status: string;
  updated: string | null;
  url: string | null;
}
export interface PreviewStateDto {
  supported: boolean;
  sources: PreviewSourceDto[];
}
export interface PublicServerDto {
  id: string;
  playing: number;
  maxPlayers: number;
  ping: number | null;
}
export interface RectDto {
  x: number;
  y: number;
  width: number;
  height: number;
}
export interface RobloxSupportPass {
  id: string;
  credit: number;
}
export interface SettingsDto {
  scheme: string;
  available: boolean;
  shortcuts: Record<string, number>;
  notifications: NotificationsDto;
  imported: boolean;
}
export interface SlotDto {
  monitor: string;
  x: number;
  y: number;
  width: number;
  height: number;
}
export interface SpecialBadgeDto {
  id: string;
  title: string;
  robloxUserId: string;
}
export interface SpecialBadgeStateDto {
  badges: SpecialBadgeDto[];
  message: string;
  claimedBadgeId: string | null;
}
export interface SupportDto {
  configured: boolean;
  supported: boolean;
  showBadge: boolean;
  pending: boolean;
  message: string;
  robloxConfigured: boolean;
  receiptAvailable: boolean;
  verifiedTier: number;
  totalCents: number;
  recoveryAvailable: boolean;
  totalRobux: number;
  robloxTier: number;
  usdTier: number;
  robloxPasses: RobloxSupportPass[] | null;
}
export interface TelemetryDto {
  enabled: boolean;
  available: boolean;
  configured: boolean;
  queuedErrors: number;
  lastResult: string | null;
}
export interface UpdateDto {
  helperVersion: string;
  available: boolean;
  version: string | null;
  title: string | null;
  notes: string | null;
  downloadProblem: string | null;
  canDownload: boolean;
  downloaded: boolean;
  releasePage: string | null;
}
