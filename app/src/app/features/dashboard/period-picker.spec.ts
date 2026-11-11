import { TestBed } from '@angular/core/testing';
import { PeriodPicker } from './period-picker';
import { PeriodSelection } from './report-models';

describe('PeriodPicker', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [PeriodPicker] });
  });

  async function render(selection: PeriodSelection, extra: Record<string, unknown> = {}) {
    const fixture = TestBed.createComponent(PeriodPicker);
    fixture.componentRef.setInput('selection', selection);
    fixture.componentRef.setInput('yearOptions', ['2026', '2025']);
    fixture.componentRef.setInput('maxDate', '2027-11-09');
    for (const [name, value] of Object.entries(extra)) {
      fixture.componentRef.setInput(name, value);
    }
    const changes: Partial<PeriodSelection>[] = [];
    fixture.componentInstance.selectionChange.subscribe((change) => changes.push(change));
    await fixture.whenStable();
    return { element: fixture.nativeElement as HTMLElement, changes };
  }

  const month: PeriodSelection = { kind: 'currentMonth', year: '2026', from: '', to: '' };

  it('shows only the period select for a month and emits a new kind', async () => {
    const { element, changes } = await render(month);
    const select = element.querySelector<HTMLSelectElement>('#pulpit-okres')!;

    expect(Array.from(select.options).map((o) => o.textContent?.trim())).toEqual([
      'Bieżący miesiąc',
      'Poprzedni miesiąc',
      'Rok',
      'Własny zakres',
    ]);
    expect(element.querySelector('#pulpit-rok')).toBeNull();
    expect(element.querySelector('#pulpit-od')).toBeNull();

    select.value = 'year';
    select.dispatchEvent(new Event('change'));
    expect(changes).toEqual([{ kind: 'year' }]);
  });

  it('shows the year select for a year', async () => {
    const { element, changes } = await render({ ...month, kind: 'year', year: '2025' });
    const select = element.querySelector<HTMLSelectElement>('#pulpit-rok')!;

    expect(Array.from(select.options).map((o) => o.value)).toEqual(['2026', '2025']);
    expect(select.value).toBe('2025');
    select.value = '2026';
    select.dispatchEvent(new Event('change'));
    expect(changes).toEqual([{ year: '2026' }]);
  });

  it('shows date fields with the max date and messages at the fields for a custom range', async () => {
    const { element, changes } = await render(
      { ...month, kind: 'custom', from: '2026-11-10', to: '2026-11-01' },
      { errors: { from: 'Data „od” nie może być późniejsza niż data „do”.', to: null } },
    );
    const from = element.querySelector<HTMLInputElement>('#pulpit-od')!;
    const to = element.querySelector<HTMLInputElement>('#pulpit-do')!;

    expect(from.type).toBe('date');
    expect(from.max).toBe('2027-11-09');
    expect(to.max).toBe('2027-11-09');
    expect(from.getAttribute('aria-invalid')).toBe('true');
    expect(element.querySelector('#pulpit-od-blad')?.textContent).toContain(
      'Data „od” nie może być późniejsza niż data „do”.',
    );

    to.value = '2026-11-20';
    to.dispatchEvent(new Event('change'));
    expect(changes).toEqual([{ to: '2026-11-20' }]);
  });

  it('shows the 422 message as an alert under the picker', async () => {
    const { element } = await render(month, {
      rangeError: 'Za dużo danych w wybranym okresie. Wybierz krótszy okres.',
    });

    expect(element.querySelector('[role="alert"]')?.textContent).toContain(
      'Za dużo danych w wybranym okresie. Wybierz krótszy okres.',
    );
  });
});
