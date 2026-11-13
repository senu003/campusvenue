export interface HealthCheckEntry {
  name: string;
  status: 'Healthy' | 'Degraded' | 'Unhealthy';
  description?: string;
  durationMs: number;
  error?: string | null;
}

export interface HealthStatusResponse {
  status: 'Healthy' | 'Degraded' | 'Unhealthy';
  totalDurationMs: number;
  timestamp: string;
  environment: string;
  checks: HealthCheckEntry[];
}

export interface ApiRootMetadata {
  name: string;
  version: string;
  status: string;
  documentation: string;
  health: string;
}
