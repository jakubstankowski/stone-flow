import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { refundEntry } from '../../../testing/orders-testing';
import {
  EntryFormOutcome,
  RefundFormModel,
  emptyRefundForm,
  refundFormFrom,
} from './entry-form-model';
import { todayInWarsaw } from './order-form-model';
import { RefundInput } from './order-models';
import { RefundForm } from './refund-form';

@Component({
  imports: [RefundForm],
  template: `<app-refund-form [initial]="initial()" [action]="action" />`,
})
class Host {
  readonly initial = signal<RefundFormModel>(emptyRefundForm('2026-10-30'));
  readonly submitted: RefundInput[] = [];
  outcome: EntryFormOutcome = null;
  readonly action = async (input: RefundInput): Promise<EntryFormOutcome> => {
    this.submitted.push(input);
    return this.outcome;
  };
}

function type(element: HTMLElement, selector: string, value: string): void {
  const control = element.querySelector(selector) as HTMLInputElement | HTMLTextAreaElement;
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
  const submit = async () => {
    (element.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    await vi.waitFor(async () => {
      await fixture.whenStable();
      expect(element.querySelector('button[aria-busy]')).toBeNull();
    });
    await fixture.whenStable();
  };
  return { fixture, host: fixture.componentInstance, element, submit };
}

function radio(element: HTMLElement, kind: string): HTMLInputElement {
  return element.querySelector(`#zwrot-rodzaj-${kind}`) as HTMLInputElement;
}

describe('RefundForm', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('offers both kinds without a default, with described effects', async () => {
    const { element } = await setup();
    const fieldset = element.querySelector('fieldset') as HTMLFieldSetElement;
    expect(fieldset.querySelector('legend')?.textContent).toContain('Rodzaj zwrotu');
    expect(radio(element, 'salesReturn').checked).toBe(false);
    expect(radio(element, 'moneyReturn').checked).toBe(false);

    const described = radio(element, 'salesReturn').getAttribute('aria-describedby') ?? '';
    expect(element.querySelector(`#${described}`)?.textContent).toContain('Obniży sprzedaż.');
    const money = radio(element, 'moneyReturn').getAttribute('aria-describedby') ?? '';
    expect(element.querySelector(`#${money}`)?.textContent).toContain('Nie zmienia sprzedaży.');
    expect(element.querySelector('#zwrot-data')?.getAttribute('max')).toBe(todayInWarsaw());
    expect(
      Array.from(element.querySelectorAll('#zwrot-metoda option')).map((o) =>
        o.textContent?.trim(),
      ),
    ).toEqual(['Wybierz metodę', 'Przelew', 'BLIK', 'Gotówka']);
  });

  it('requires the kind and the reason', async () => {
    const { element, host, submit } = await setup();
    type(element, '#zwrot-kwota', '100');
    choose(element, '#zwrot-metoda', 'transfer');
    type(element, '#zwrot-powod', '   ');
    await submit();

    expect(host.submitted).toHaveLength(0);
    const summary = element.querySelector('[role="alert"][tabindex="-1"]')?.textContent ?? '';
    expect(summary).toContain('Wybierz rodzaj zwrotu.');
    expect(summary).toContain('Podaj powód zwrotu.');
    expect(element.querySelector('#zwrot-rodzaj-blad')?.textContent).toContain(
      'Wybierz rodzaj zwrotu.',
    );
    expect(radio(element, 'salesReturn').getAttribute('aria-invalid')).toBe('true');
  });

  it('sends the chosen kind and a trimmed reason', async () => {
    const { element, host, submit } = await setup();
    radio(element, 'salesReturn').click();
    type(element, '#zwrot-kwota', '1 200,5');
    choose(element, '#zwrot-metoda', 'cash');
    type(element, '#zwrot-powod', ' Oddany towar ');
    await submit();

    expect(host.submitted).toEqual([
      {
        kind: 'salesReturn',
        amount: '1200.50',
        date: '2026-10-30',
        method: 'cash',
        reason: 'Oddany towar',
      },
    ]);
  });

  it('edits an entry, including a change of kind', async () => {
    const { element, host, submit } = await setup((host) =>
      host.initial.set(refundFormFrom(refundEntry())),
    );
    expect(radio(element, 'moneyReturn').checked).toBe(true);
    radio(element, 'salesReturn').click();
    await submit();
    expect(host.submitted[0].kind).toBe('salesReturn');
    expect(host.submitted[0].amount).toBe('100.00');
  });
});
