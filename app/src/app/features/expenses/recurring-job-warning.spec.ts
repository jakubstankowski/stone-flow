import { recurringJob } from '../../../testing/expenses-testing';
import { JobStatus } from './jobs-api';
import { recurringJobStatus, recurringJobWarning } from './recurring-job-warning';

const NOW = Date.parse('2026-11-08T12:00:00Z');
const HOUR = 60 * 60 * 1000;
const MINUTE = 60 * 1000;

function iso(ms: number): string {
  return new Date(ms).toISOString();
}

describe('recurringJobWarning', () => {
  it('warns while a retry is running after earlier failures', () => {
    const runAt = iso(NOW - 10 * MINUTE);
    const status = recurringJob({
      lastRun: { status: 'running', startedAtUtc: runAt, finishedAtUtc: null, error: null },
      lastSuccessStartedAtUtc: null,
      consecutiveFailures: 2,
    });

    expect(recurringJobWarning(status, NOW)).toEqual({
      kind: 'warning',
      shownAtUtc: runAt,
      lastSuccessStartedAtUtc: null,
    });
  });

  it('warns about a failed last run with its start time and the last success', () => {
    const success = iso(NOW - 48 * HOUR);
    const runAt = iso(NOW - HOUR);
    const status = recurringJob({
      lastRun: { status: 'failed', startedAtUtc: runAt, finishedAtUtc: runAt, error: 'x' },
      lastSuccessStartedAtUtc: success,
    });

    expect(recurringJobWarning(status, NOW)).toEqual({
      kind: 'warning',
      shownAtUtc: runAt,
      lastSuccessStartedAtUtc: success,
    });
  });

  it('warns about an abandoned last run even with a fresh success', () => {
    const runAt = iso(NOW - HOUR);
    const status = recurringJob({
      lastRun: { status: 'abandoned', startedAtUtc: runAt, finishedAtUtc: null, error: null },
      lastSuccessStartedAtUtc: iso(NOW - 2 * HOUR),
    });

    expect(recurringJobWarning(status, NOW).kind).toBe('warning');
  });

  it('warns about the first failed run without any success', () => {
    const runAt = iso(NOW - HOUR);
    const status = recurringJob({
      lastRun: { status: 'failed', startedAtUtc: runAt, finishedAtUtc: runAt, error: null },
    });

    expect(recurringJobWarning(status, NOW)).toEqual({
      kind: 'warning',
      shownAtUtc: runAt,
      lastSuccessStartedAtUtc: null,
    });
  });

  it('does not warn at 24 h 59 min after the last success', () => {
    const success = iso(NOW - (24 * HOUR + 59 * MINUTE));
    const status = recurringJob({
      lastRun: { status: 'succeeded', startedAtUtc: success, finishedAtUtc: success, error: null },
      lastSuccessStartedAtUtc: success,
    });

    expect(recurringJobWarning(status, NOW).kind).toBe('none');
  });

  it('warns at 25 h 1 min after the last success, showing the success time', () => {
    const success = iso(NOW - (25 * HOUR + MINUTE));
    const status = recurringJob({
      lastRun: { status: 'succeeded', startedAtUtc: success, finishedAtUtc: success, error: null },
      lastSuccessStartedAtUtc: success,
    });

    expect(recurringJobWarning(status, NOW)).toEqual({
      kind: 'warning',
      shownAtUtc: success,
      lastSuccessStartedAtUtc: success,
    });
  });

  it('shows the running run time when the success is too old', () => {
    const success = iso(NOW - 30 * HOUR);
    const runAt = iso(NOW - MINUTE);
    const status = recurringJob({
      isRunning: true,
      lastRun: { status: 'running', startedAtUtc: runAt, finishedAtUtc: null, error: null },
      lastSuccessStartedAtUtc: success,
    });

    expect(recurringJobWarning(status, NOW)).toEqual({
      kind: 'warning',
      shownAtUtc: runAt,
      lastSuccessStartedAtUtc: success,
    });
  });

  it('does not warn for the first running run without a success', () => {
    const status = recurringJob({
      isRunning: true,
      lastRun: { status: 'running', startedAtUtc: iso(NOW), finishedAtUtc: null, error: null },
    });

    expect(recurringJobWarning(status, NOW).kind).toBe('none');
  });

  it('does not warn for a running run after a fresh success', () => {
    const status = recurringJob({
      isRunning: true,
      lastRun: { status: 'running', startedAtUtc: iso(NOW), finishedAtUtc: null, error: null },
      lastSuccessStartedAtUtc: iso(NOW - 2 * HOUR),
    });

    expect(recurringJobWarning(status, NOW).kind).toBe('none');
  });

  it('does not warn for a job without runs or without its entry', () => {
    expect(recurringJobWarning(recurringJob(), NOW).kind).toBe('none');
    expect(recurringJobWarning(undefined, NOW).kind).toBe('none');
  });

  it('picks the recurring-expenses entry from the job list', () => {
    const other: JobStatus = recurringJob({ key: 'woocommerce-sync' });
    const own = recurringJob();

    expect(recurringJobStatus([other, own])).toBe(own);
    expect(recurringJobStatus([other])).toBeUndefined();
  });
});
