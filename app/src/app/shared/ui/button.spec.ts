import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Button, ButtonVariant } from './button';

@Component({
  imports: [Button],
  template: `<form (submit)="submitted = submitted + 1; $event.preventDefault()">
    <app-button
      [variant]="variant()"
      [type]="type()"
      [busy]="busy()"
      [disabled]="disabled()"
      (pressed)="pressed = pressed + 1"
      >Zapisz</app-button
    >
  </form>`,
})
class Host {
  readonly variant = signal<ButtonVariant>('primary');
  readonly type = signal<'button' | 'submit'>('button');
  readonly busy = signal(false);
  readonly disabled = signal(false);
  pressed = 0;
  submitted = 0;
}

async function setup() {
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  const button = (fixture.nativeElement as HTMLElement).querySelector(
    'button',
  ) as HTMLButtonElement;
  return { fixture, host: fixture.componentInstance, button };
}

describe('Button', () => {
  it('renders the variant classes and the projected label', async () => {
    const { button } = await setup();
    expect(button.classList).toContain('btn-primary');
    expect(button.classList).toContain('min-h-11');
    expect(button.textContent?.trim()).toBe('Zapisz');
    expect(button.type).toBe('button');
  });

  it('emits pressed on click', async () => {
    const { host, button } = await setup();
    button.click();
    expect(host.pressed).toBe(1);
  });

  it('blocks repeated clicks and submit while busy, keeping the button focusable', async () => {
    const { fixture, host, button } = await setup();
    host.type.set('submit');
    host.busy.set(true);
    await fixture.whenStable();

    button.click();
    button.click();

    expect(host.pressed).toBe(0);
    expect(host.submitted).toBe(0);
    expect(button.disabled).toBe(false);
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(button.textContent).toContain('Zapisywanie…');
  });

  it('uses the native disabled state for an unavailable action', async () => {
    const { fixture, host, button } = await setup();
    host.disabled.set(true);
    await fixture.whenStable();

    expect(button.disabled).toBe(true);
    expect(button.hasAttribute('aria-busy')).toBe(false);
  });

  it('maps danger and secondary variants', async () => {
    const { fixture, host, button } = await setup();
    host.variant.set('danger');
    await fixture.whenStable();
    expect(button.classList).toContain('btn-error');

    host.variant.set('secondary');
    await fixture.whenStable();
    expect(button.classList).toContain('bg-base-100');
  });
});

describe('Button busy state recovery', () => {
  it('emits again once it is no longer busy', async () => {
    const { fixture, host, button } = await setup();
    host.busy.set(true);
    await fixture.whenStable();
    button.click();

    host.busy.set(false);
    await fixture.whenStable();
    button.click();

    expect(host.pressed).toBe(1);
    expect(button.hasAttribute('aria-disabled')).toBe(false);
    expect(button.textContent?.trim()).toBe('Zapisz');
  });
});
