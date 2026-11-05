import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ApiError } from '../../core/http/api-error';
import { EntryFormOutcome, PaymentFormModel, emptyPaymentForm } from './entry-form-model';
import { todayInWarsaw } from './order-form-model';
import { PaymentInput } from './order-models';
import { PaymentForm } from './payment-form';

@Component({
  imports: [PaymentForm],
  template: `<app-payment-form
    [initial]="initial()"
    [cancelledWarning]="cancelled()"
    [action]="action"
  />`,
})
class Host {
  readonly initial = signal<PaymentFormModel>(emptyPaymentForm('2026-10-29'));
  readonly cancelled = signal(false);
  readonly submitted: PaymentInput[] = [];
  outcome: EntryFormOutcome = null;
  release: (() => void) | null = null;
  hold = false;
  readonly action = async (input: PaymentInput): Promise<EntryFormOutcome> => {
    this.submitted.push(input);
    if (this.hold) {
      await new Promise<void>((resolve) => (this.release = resolve));
    }
    return this.outcome;
  };
}

function type(element: HTMLElement, selector: string, value: string): void {
  const control = element.querySelector(selector) as HTMLInputElement;
  control.value = value;
  control.dispatchEvent(new Event('input', { bubbles: true }));
}

function choose(element: HTMLElement, selector: string, value: string): void {
  const select = element.querySelector(selector) as HTMLSelectElement;
  select.value = value;
  select.dispatchEvent(new Event('input', { bubbles: true }));
  select.dispatchEvent(new Event('change', { bubbles: true }));
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
  return { fixture, host: fixture.componentInstance, element, stable, submit };
}

function summary(element: HTMLElement): HTMLElement | null {
  return element.querySelector('[role="alert"][tabindex="-1"]');
}

describe('PaymentForm', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('focuses the amount and limits the date to today in Europe/Warsaw', async () => {
    const { element } = await setup();
    await vi.waitFor(() => expect(document.activeElement?.id).toBe('wplata-kwota'));
    expect(element.querySelector('#wplata-data')?.getAttribute('max')).toBe(todayInWarsaw());
    expect(element.querySelector('#wplata-kwota')?.getAttribute('aria-required')).toBe('true');
  });

  it('shows „Przyjął” only for cash and clears it when the method changes', async () => {
    const { element, host, stable, submit } = await setup();
    expect(element.querySelector('#wplata-przyjal')).toBeNull();

    choose(element, '#wplata-metoda', 'cash');
    await stable();
    type(element, '#wplata-przyjal', 'Anna');
    choose(element, '#wplata-metoda', 'transfer');
    await stable();
    expect(element.querySelector('#wplata-przyjal')).toBeNull();
    choose(element, '#wplata-metoda', 'cash');
    await stable();
    expect((element.querySelector('#wplata-przyjal') as HTMLInputElement).value).toBe('');

    type(element, '#wplata-przyjal', ' Anna ');
    type(element, '#wplata-kwota', '300');
    await submit();
    expect(host.submitted).toEqual([
      { amount: '300.00', date: '2026-10-29', method: 'cash', receivedBy: 'Anna' },
    ]);
  });

  it('blocks a zero amount, a future date and a missing method', async () => {
    const { element, host, submit } = await setup();
    type(element, '#wplata-kwota', '0');
    type(element, '#wplata-data', '2999-01-01');
    await submit();

    expect(host.submitted).toHaveLength(0);
    const text = summary(element)?.textContent ?? '';
    expect(text).toContain('większą od 0 zł');
    expect(text).toContain('Data nie może być późniejsza niż dzisiaj.');
    expect(text).toContain('Wybierz metodę.');
  });

  it('shows the cancelled-order warning before saving', async () => {
    const { element } = await setup((host) => host.cancelled.set(true));
    expect(element.textContent).toContain(
      'Zamówienie jest anulowane. Wpłata zwiększy kwotę do zwrotu.',
    );
  });

  it('sends one request while saving', async () => {
    const { element, host, stable } = await setup((host) => (host.hold = true));
    type(element, '#wplata-kwota', '300');
    choose(element, '#wplata-metoda', 'blik');
    const button = element.querySelector('button[type="submit"]') as HTMLButtonElement;
    button.click();
    await vi.waitFor(async () => {
      await stable();
      expect(button.getAttribute('aria-busy')).toBe('true');
    });
    button.click();
    expect(host.submitted).toHaveLength(1);
    expect(element.textContent).toContain('Zapisywanie…');
    host.release?.();
    await vi.waitFor(async () => {
      await stable();
      expect(button.getAttribute('aria-busy')).toBeNull();
    });
  });

  it('maps API field errors and 422 messages', async () => {
    const validation: ApiError = {
      kind: 'validation',
      fieldErrors: { receivedby: ['Pole tylko przy gotówce.'], version: ['Brak wersji.'] },
      detail: null,
    };
    const { element, host, stable, submit } = await setup((host) => {
      host.outcome = { error: validation };
    });
    type(element, '#wplata-kwota', '300');
    choose(element, '#wplata-metoda', 'cash');
    await stable();
    await submit();
    expect(summary(element)?.textContent).toContain('Pole tylko przy gotówce.');
    expect(summary(element)?.textContent).toContain('Brak wersji.');
    expect(element.querySelector('#wplata-przyjal-blad')?.textContent).toContain(
      'Pole tylko przy gotówce.',
    );

    host.outcome = { message: 'Zapłacono spadłoby poniżej 0 zł. Najpierw popraw lub usuń zwrot.' };
    type(element, '#wplata-przyjal', 'Anna');
    await submit();
    expect(host.submitted).toHaveLength(2);
    expect(summary(element)?.textContent).toContain('Najpierw popraw lub usuń zwrot.');
    expect((element.querySelector('#wplata-kwota') as HTMLInputElement).value).toBe('300');
  });
});
