import { Component, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ConfirmDialog } from './confirm-dialog';

@Component({
  imports: [ConfirmDialog],
  template: `<main id="tresc" tabindex="-1">
    @if (showTrigger()) {
      <button id="wyzwalacz" type="button">Usuń wydatek</button>
    }
    <app-confirm-dialog title="Usunąć wydatek?" confirmLabel="Usuń">
      Wydatek zostanie usunięty z listy.
    </app-confirm-dialog>
  </main>`,
})
class Host {
  readonly showTrigger = signal(true);
  readonly dialog = viewChild.required(ConfirmDialog);
}

async function setup() {
  const fixture = TestBed.createComponent(Host);
  document.body.appendChild(fixture.nativeElement);
  await fixture.whenStable();
  const element = fixture.nativeElement as HTMLElement;
  const trigger = element.querySelector('#wyzwalacz') as HTMLButtonElement;
  trigger.focus();
  return {
    fixture,
    host: fixture.componentInstance,
    element,
    trigger,
    dialogElement: element.querySelector('dialog') as HTMLDialogElement,
    buttons: () => Array.from(element.querySelectorAll<HTMLButtonElement>('dialog button')),
  };
}

describe('ConfirmDialog', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('opens with the title, content and focus on Cancel', async () => {
    const { host, dialogElement, buttons } = await setup();

    void host.dialog().open();

    expect(dialogElement.open).toBe(true);
    expect(dialogElement.getAttribute('aria-labelledby')).toBeTruthy();
    expect(dialogElement.textContent).toContain('Usunąć wydatek?');
    expect(dialogElement.textContent).toContain('Wydatek zostanie usunięty z listy.');
    expect(document.activeElement).toBe(buttons()[0]);
    expect(buttons()[0].textContent?.trim()).toBe('Anuluj');
  });

  it('resolves true on the action and returns focus to the trigger', async () => {
    const { host, trigger, dialogElement, buttons } = await setup();
    const result = host.dialog().open();

    buttons()[1].click();

    await expect(result).resolves.toBe(true);
    expect(dialogElement.open).toBe(false);
    expect(document.activeElement).toBe(trigger);
  });

  it('resolves false on Cancel', async () => {
    const { host, trigger, buttons } = await setup();
    const result = host.dialog().open();

    buttons()[0].click();

    await expect(result).resolves.toBe(false);
    expect(document.activeElement).toBe(trigger);
  });

  it('resolves false on Escape (cancel event)', async () => {
    const { host, trigger, dialogElement } = await setup();
    const result = host.dialog().open();

    const cancel = new Event('cancel', { cancelable: true });
    dialogElement.dispatchEvent(cancel);

    await expect(result).resolves.toBe(false);
    expect(cancel.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(trigger);
  });

  it('moves focus to the main content when the trigger no longer exists', async () => {
    const { fixture, host, element, buttons } = await setup();
    const result = host.dialog().open();

    host.showTrigger.set(false);
    await fixture.whenStable();
    buttons()[1].click();

    await expect(result).resolves.toBe(true);
    expect(document.activeElement).toBe(element.querySelector('#tresc'));
  });
});
