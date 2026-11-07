import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { salesCorrectionEntry } from '../../../testing/orders-testing';
import {
  EntryFormOutcome,
  SalesCorrectionFormModel,
  emptySalesCorrectionForm,
  salesCorrectionFormFrom,
} from './entry-form-model';
import { todayInWarsaw } from '../../shared/format/pl-format';
import { SalesCorrectionInput } from './order-models';
import { SalesCorrectionForm } from './sales-correction-form';

@Component({
  imports: [SalesCorrectionForm],
  template: `<app-sales-correction-form [initial]="initial()" [action]="action" />`,
})
class Host {
  readonly initial = signal<SalesCorrectionFormModel>(emptySalesCorrectionForm('2026-10-31'));
  readonly submitted: SalesCorrectionInput[] = [];
  outcome: EntryFormOutcome = null;
  release: (() => void) | null = null;
  hold = false;
  readonly action = async (input: SalesCorrectionInput): Promise<EntryFormOutcome> => {
    this.submitted.push(input);
    if (this.hold) {
      await new Promise<void>((resolve) => (this.release = resolve));
    }
    return this.outcome;
  };
}

function type(element: HTMLElement, selector: string, value: string): void {
  const control = element.querySelector(selector) as HTMLInputElement | HTMLTextAreaElement;
  control.value = value;
  control.dispatchEvent(new Event('input', { bubbles: true }));
}

async function setup(configure: (host: Host) => void = () => undefined) {
  const fixture = TestBed.createComponent(Host);
  configure(fixture.componentInstance);
  document.body.appendChild(fixture.nativeElement);
  await fixture.whenStable();
  const element = fixture.nativeElement as HTMLElement;
  const stable = () => fixture.whenStable();
  const submit = async () => {
    (element.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    await vi.waitFor(async () => {
      await fixture.whenStable();
      expect(element.querySelector('button[aria-busy]')).toBeNull();
    });
    await fixture.whenStable();
  };
  return { host: fixture.componentInstance, element, stable, submit };
}

function summary(element: HTMLElement): string {
  return element.querySelector('[role="alert"][tabindex="-1"]')?.textContent ?? '';
}

describe('SalesCorrectionForm', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('explains the correction, focuses the amount and limits the date', async () => {
    const { element } = await setup();
    expect(element.textContent).toContain(
      'Korekta służy do oddania towaru bez zwrotu pieniędzy: obniża sprzedaż, nie tworzy wypływu i nie zmienia kwoty Zapłacono.',
    );
    await vi.waitFor(() => expect(document.activeElement?.id).toBe('korekta-kwota'));
    expect(element.querySelector('#korekta-data')?.getAttribute('max')).toBe(todayInWarsaw());
    expect(element.querySelector('#korekta-metoda')).toBeNull();
  });

  it('requires a positive amount, a past date and a reason', async () => {
    const { element, host, submit } = await setup();
    type(element, '#korekta-kwota', '0');
    type(element, '#korekta-data', '2999-01-01');
    await submit();

    expect(host.submitted).toHaveLength(0);
    expect(summary(element)).toContain('większą od 0 zł');
    expect(summary(element)).toContain('Data nie może być późniejsza niż dzisiaj.');
    expect(summary(element)).toContain('Podaj powód korekty.');
  });

  it('sends a normalized input and shows the limit message from the API', async () => {
    const { element, host, submit } = await setup((host) => {
      host.outcome = {
        message:
          'Suma zwrotów sprzedaży i korekt nie może przekroczyć wartości zamówienia. Dostępna kwota: 1000,00 zł.',
      };
    });
    type(element, '#korekta-kwota', '1 000,01');
    type(element, '#korekta-powod', '  Oddany towar ');
    await submit();

    expect(host.submitted).toEqual([
      { amount: '1000.01', date: '2026-10-31', reason: 'Oddany towar' },
    ]);
    expect(summary(element)).toContain('Dostępna kwota: 1000,00 zł.');
    expect((element.querySelector('#korekta-kwota') as HTMLInputElement).value).toBe('1 000,01');
  });

  it('edits an entry without arithmetic and sends one request while saving', async () => {
    const { element, host, stable } = await setup((host) => {
      host.initial.set(salesCorrectionFormFrom(salesCorrectionEntry()));
      host.hold = true;
    });
    expect((element.querySelector('#korekta-kwota') as HTMLInputElement).value).toBe('500,00');
    const button = element.querySelector('button[type="submit"]') as HTMLButtonElement;
    button.click();
    await vi.waitFor(async () => {
      await stable();
      expect(button.getAttribute('aria-busy')).toBe('true');
    });
    button.click();
    expect(host.submitted).toEqual([
      { amount: '500.00', date: '2026-10-31', reason: 'Oddany towar bez zwrotu pieniędzy' },
    ]);
    host.release?.();
    await vi.waitFor(async () => {
      await stable();
      expect(button.getAttribute('aria-busy')).toBeNull();
    });
  });
});
