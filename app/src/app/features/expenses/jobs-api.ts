import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Kod przebiegu zadania (SF-11). */
export type JobRunStatusCode = 'running' | 'succeeded' | 'failed' | 'abandoned';

export interface JobRun {
  readonly status: JobRunStatusCode;
  readonly startedAtUtc: string;
  readonly finishedAtUtc: string | null;
  /** Opis błędu z historii przebiegów — interfejs go nie pokazuje. */
  readonly error: string | null;
}

/** `JobStatusDto` z `GET /api/jobs` (SF-11). */
export interface JobStatus {
  readonly key: string;
  readonly schedule: string;
  readonly isRunning: boolean;
  readonly lastRun: JobRun | null;
  readonly lastSuccessStartedAtUtc: string | null;
  readonly consecutiveFailures: number;
  readonly nextAttemptAtUtc: string | null;
}

/** `GET /api/jobs` (sesja wymagana). Błędy jako `HttpErrorResponse`. */
@Injectable({ providedIn: 'root' })
export class JobsApi {
  private readonly http = inject(HttpClient);

  list(): Promise<readonly JobStatus[]> {
    return firstValueFrom(this.http.get<readonly JobStatus[]>('/api/jobs'));
  }
}
