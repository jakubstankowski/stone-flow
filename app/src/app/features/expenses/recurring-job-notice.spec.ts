import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { nextRequest, provideTestHttp, respondProblem } from '../../../testing/auth-testing';
import { recurringJob } from '../../../testing/expenses-testing';
import { formatDateTime } from '../../shared/format/pl-format';
import { RECURRING_JOB_UNKNOWN_TEXT, RecurringJobNotice } from './recurring-job-notice';

describe('RecurringJobNotice', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RecurringJobNotice],
      providers: [provideTestHttp()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(respond: (request: Awaited<ReturnType<typeof nextRequest>>) => void) {
    const fixture = TestBed.createComponent(RecurringJobNotice);
    const element = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
    expect(element.textContent?.trim()).toBe('');
    respond(await nextRequest({ method: 'GET', url: '/api/jobs' }));
    await vi.waitFor(async () => {
      await fixture.whenStable();
    });
    return element;
  }

  it('shows a failed run with its time and the last success in a status region', async () => {
    const runAt = '2026-11-08T23:10:00Z';
    const success = '2026-11-06T23:10:00Z';
    const element = await render((request) =>
      request.flush([
        recurringJob({
          lastRun: { status: 'failed', startedAtUtc: runAt, finishedAtUtc: runAt, error: 'tajne' },
          lastSuccessStartedAtUtc: success,
        }),
      ]),
    );

    await vi.waitFor(() => expect(element.querySelector('[role="status"]')).not.toBeNull());
    const text = element.textContent ?? '';
    expect(text).toContain(
      `Automatyczne tworzenie wydatków cyklicznych nie powiodło się (${formatDateTime(runAt)}). Koszty mogą być zaniżone.`,
    );
    expect(text).toContain(`Ostatnie udane tworzenie: ${formatDateTime(success)}`);
    expect(text).not.toContain('tajne');
    expect(element.querySelector('h2')).not.toBeNull();
    expect(element.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('shows „Brak danych” when the job never succeeded', async () => {
    const runAt = '2026-11-08T23:10:00Z';
    const element = await render((request) =>
      request.flush([
        recurringJob({
          lastRun: { status: 'abandoned', startedAtUtc: runAt, finishedAtUtc: null, error: null },
        }),
      ]),
    );

    await vi.waitFor(() =>
      expect(element.textContent).toContain('Ostatnie udane tworzenie: Brak danych'),
    );
  });

  it('shows nothing for a job without runs', async () => {
    const element = await render((request) => request.flush([recurringJob()]));

    expect(element.textContent?.trim()).toBe('');
  });

  it('shows the cautious state when the read fails', async () => {
    const element = await render((request) => respondProblem(request, 503));

    await vi.waitFor(() => expect(element.textContent).toContain(RECURRING_JOB_UNKNOWN_TEXT));
  });

  it('shows nothing for a 4xx error, which says nothing about the job', async () => {
    const element = await render((request) => respondProblem(request, 403));

    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(element.textContent).not.toContain(RECURRING_JOB_UNKNOWN_TEXT);
    expect(element.textContent?.trim()).toBe('');
  });

  it('reads the jobs only once, without background refreshes', async () => {
    await render((request) => request.flush([recurringJob()]));

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'a' }));
    document.dispatchEvent(new Event('visibilitychange'));
    await new Promise((resolve) => setTimeout(resolve, 20));
    http.expectNone('/api/jobs');
  });
});
