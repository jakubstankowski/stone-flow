import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Field } from './field';

@Component({
  imports: [Field],
  template: `<app-field
    label="Kwota brutto"
    fieldId="kwota"
    [required]="required()"
    [hint]="hint()"
    [error]="error()"
  >
    <input #control class="input" type="text" />
  </app-field>`,
})
class Host {
  readonly required = signal(true);
  readonly hint = signal<string | null>('Na przykład 120,00');
  readonly error = signal<string | null>(null);
}

async function setup() {
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  const element = fixture.nativeElement as HTMLElement;
  return {
    fixture,
    host: fixture.componentInstance,
    element,
    input: element.querySelector('input') as HTMLInputElement,
  };
}

describe('Field', () => {
  it('associates the label with the projected control', async () => {
    const { element, input } = await setup();
    const label = element.querySelector('label') as HTMLLabelElement;

    expect(input.id).toBe('kwota');
    expect(label.htmlFor).toBe('kwota');
    expect(label.textContent).toContain('Kwota brutto');
  });

  it('marks a required field visibly and for assistive technology', async () => {
    const { element, input } = await setup();

    expect(element.querySelector('label')?.textContent).toContain('(wymagane)');
    expect(input.getAttribute('aria-required')).toBe('true');
  });

  it('describes the control with the hint and the error', async () => {
    const { fixture, host, element, input } = await setup();
    expect(input.getAttribute('aria-describedby')).toBe('kwota-pomoc');
    expect(input.hasAttribute('aria-invalid')).toBe(false);

    host.error.set('Podaj kwotę większą od 0 zł.');
    await fixture.whenStable();

    expect(input.getAttribute('aria-describedby')).toBe('kwota-pomoc kwota-blad');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(element.querySelector('#kwota-blad')?.textContent).toContain('Podaj kwotę');
  });

  it('removes attributes that no longer apply', async () => {
    const { fixture, host, element, input } = await setup();
    host.required.set(false);
    host.hint.set(null);
    await fixture.whenStable();

    expect(input.hasAttribute('aria-required')).toBe(false);
    expect(input.hasAttribute('aria-describedby')).toBe(false);
    expect(element.querySelector('label')?.textContent).not.toContain('(wymagane)');
  });
});
