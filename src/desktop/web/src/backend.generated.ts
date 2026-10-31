// Generated from src/desktop/backend/Contracts.cs. Do not edit.
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
}
export interface CheckDto {
  id: string;
  name: string;
  state: string;
  code: string;
  message: string;
  action: string;
}
export interface HealthDto {
  overall: string;
  helperVersion: string;
  browserIntegrationEnabled: boolean | null;
  checks: CheckDto[];
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
}
export interface InstanceListDto {
  available: boolean;
  unavailable: number;
  instances: InstanceDto[];
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
export interface RectDto {
  x: number;
  y: number;
  width: number;
  height: number;
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
export interface UpdateDto {
  helperVersion: string;
  available: boolean;
  version: string | null;
  title: string | null;
  notes: string | null;
  downloadProblem: string | null;
  canDownload: boolean;
  downloaded: boolean;
}
