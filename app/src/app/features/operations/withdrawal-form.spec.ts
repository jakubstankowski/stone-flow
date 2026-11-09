import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { typeInto } from '../../../testing/auth-testing';
import { WithdrawalForm, WithdrawalFormAction, WithdrawalFormOutcome } from './withdrawal-form';
import {
  WITHDRAWAL_MESSAGES,
  WithdrawalFormModel,
  emptyWithdrawalForm,
} from './withdrawal-form-model';
import { WithdrawalInput } from './withdrawal-models';

@Component({
  imports: [WithdrawalForm],
  template: `<app-withdrawal-form
    [initial]="initial()"
    [action]="action"
    [people]="people()"
    (cancelled)="cancelCount = cancelCount + 1"
  />`,
})
class Host {
  readonly initial = signal<WithdrawalFormModel>(emptyWithdrawalForm('2026-11-05'));
  readonly people = signal<readonly string[]>(['Właściciel A', 'Wspólnik B']);
  cancelCount = 0;
  calls: WithdrawalInput[] = [];
  respond: (input: WithdrawalInput) => Promise<WithdrawalFormOutcome> = async () => null;
  readonly action: WithdrawalFormAction = (input) => {
    this.calls.push(input);
    return this.respond(input);
  };
}

function buttonByText(root: ParentNode, text: string): HTMLButtonElement {
  const button = Array.from(root.querySelectorAll('button')).find(
    (candidate) => candidate.textContent?.trim() === text,
  );
  if (!button) {
    throw new Error(`Brak przycisku „${text}”`);
  }
  return button;
}

function choose(select: HTMLSelectElement, value: string): void {
  select.value = value;
  select.dispatchEvent(new Event('input', { bubbles: true }));
  select.dispatchEvent(new Event('change', { bubbles: true }));
}

describe('WithdrawalForm', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  async function render() {
    const fixture = TestBed.createComponent(Host);
    document.body.appendChild(fixture.nativeElement);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const settle = async (check: () => void) =>
      vi.waitFor(async () => {
        await fixture.whenStable();
        check();
      });
    return { fixture, host: fixture.componentInstance, element, settle };
  }

  function fill(element: HTMLElement): void {
    typeInto(element.querySelector('#wyplata-osoba') as HTMLInputElement, ' Właściciel A ');
    typeInto(element.querySelector('#wyplata-kwota') as HTMLInputElement, '2 000,00');
    choose(element.querySelector('#wyplata-metoda') as HTMLSelectElement, 'blik');
  }

  it('offers people suggestions and has no default method', async () => {
    const { element } = await render();
    const person = element.querySelector('#wyplata-osoba') as HTMLInputElement;
    const list = element.querySelector(`datalist#${person.getAttribute('list')}`);
    const method = element.querySelector('#wyplata-metoda') as HTMLSelectElement;

    expect(Array.from(list?.querySelectorAll('option') ?? []).map((o) => o.value)).toEqual([
      'Właściciel A',
      'Wspólnik B',
    ]);
    expect(method.value).toBe('');
    expect(method.options[0].textContent?.trim()).toBe('Wybierz…');
    expect((element.querySelector('#wyplata-data') as HTMLInputElement).value).toBe('2026-11-05');
  });

  it('shows field errors and the summary and focuses the first invalid field', async () => {
    const { host, element, settle } = await render();

    buttonByText(element, 'Zapisz').click();
    await settle(() => expect(element.textContent).toContain('Popraw błędy w formularzu'));

    expect(element.textContent).toContain(WITHDRAWAL_MESSAGES.personRequired);
    expect(element.textContent).toContain(WITHDRAWAL_MESSAGES.amountRequired);
    expect(element.textContent).toContain(WITHDRAWAL_MESSAGES.methodRequired);
    expect(
      (element.querySelector('#wyplata-osoba') as HTMLInputElement).getAttribute('aria-invalid'),
    ).toBe('true');
    await settle(() => expect(document.activeElement?.id).toBe('wyplata-osoba'));
    expect(host.calls).toEqual([]);
  });

  it('sends the mapped input and pins API errors to their fields', async () => {
    const { host, element, settle } = await render();
    host.respond = async () => ({
      error: {
        kind: 'validation',
        fieldErrors: { amount: ['Kwota jest zbyt duża.'], version: ['Brak wersji.'] },
        detail: null,
      },
    });

    fill(element);
    buttonByText(element, 'Zapisz').click();
    await settle(() => expect(element.textContent).toContain('Popraw błędy w formularzu'));

    expect(host.calls).toEqual([
      { person: 'Właściciel A', amount: '2000.00', withdrawnOn: '2026-11-05', method: 'blik' },
    ]);
    const amountField = element.querySelector('#wyplata-kwota')?.closest('app-field');
    expect(amountField?.textContent).toContain('Kwota jest zbyt duża.');
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('Brak wersji.');
    await settle(() => expect(document.activeElement?.id).toBe('wyplata-kwota'));
  });

  it('shows a network error in the summary', async () => {
    const { host, element, settle } = await render();
    host.respond = async () => ({ error: { kind: 'network' } });

    fill(element);
    buttonByText(element, 'Zapisz').click();

    await settle(() =>
      expect(element.querySelector('[role="alert"]')?.textContent).toContain(
        'Nie udało się połączyć z serwerem.',
      ),
    );
  });

  it('blocks a second submit while saving', async () => {
    const { host, element, settle } = await render();
    let finish: (outcome: WithdrawalFormOutcome) => void = () => undefined;
    host.respond = () => new Promise((resolve) => (finish = resolve));

    fill(element);
    buttonByText(element, 'Zapisz').click();
    await settle(() => expect(element.textContent).toContain('Zapisywanie…'));
    (element.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    await settle(() => expect(host.calls).toHaveLength(1));

    finish(null);
    await settle(() => expect(element.textContent).not.toContain('Zapisywanie…'));
    expect(host.calls).toHaveLength(1);
  });

  it('emits cancel', async () => {
    const { host, element } = await render();

    buttonByText(element, 'Anuluj').click();

    expect(host.cancelCount).toBe(1);
  });
});
