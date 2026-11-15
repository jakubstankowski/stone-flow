import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { orderDetails } from '../../../testing/orders-testing';
import { ApiError } from '../../core/http/api-error';
import { formatPln } from '../../shared/format/pl-format';
import { OrderForm, OrderFormOutcome } from './order-form';
import { OrderFormModel, emptyOrderForm, orderFormFrom } from './order-form-model';
import { OrderInput } from './order-models';

@Component({
  imports: [OrderForm],
  template: `<app-order-form
    [initial]="initial()"
    [shopOwnedFields]="shopOwned()"
    [cancelled]="cancelled()"
    [customerAnonymized]="customerAnonymized()"
    [action]="action"
  />`,
})
class Host {
  readonly initial = signal<OrderFormModel>(emptyOrderForm('2026-10-28'));
  readonly shopOwned = signal<readonly string[]>([]);
  readonly cancelled = signal(false);
  readonly customerAnonymized = signal(false);
  readonly submitted: OrderInput[] = [];
  outcome: OrderFormOutcome = null;
  readonly action = async (input: OrderInput): Promise<OrderFormOutcome> => {
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

function fillRequired(element: HTMLElement): void {
  type(element, '#zamowienie-klient-nazwa', '  Jan Testowy ');
  choose(element, '#zamowienie-kanal', 'phone');
  type(element, '#zamowienie-wartosc', '1 234,5');
}

function summary(element: HTMLElement): HTMLElement | null {
  return element.querySelector('[role="alert"][tabindex="-1"]');
}

describe('OrderForm', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('offers the statuses without „Anulowane”', async () => {
    const { element } = await setup();
    const options = Array.from(
      element.querySelectorAll<HTMLOptionElement>('#zamowienie-status option'),
    ).map((option) => option.textContent?.trim());

    expect(options).toEqual(['W trakcie', 'Zakończone', 'Problem / reklamacja']);
  });

  it('groups the fields into the four sections with required markers', async () => {
    const { element } = await setup();
    const legends = Array.from(element.querySelectorAll('legend')).map((legend) =>
      legend.textContent?.trim(),
    );

    expect(legends).toEqual(['Klient', 'Zamówienie', 'Realizacja', 'Uwagi']);
    expect(element.querySelector('#zamowienie-klient-nazwa')?.getAttribute('aria-required')).toBe(
      'true',
    );
    expect(element.querySelector('#zamowienie-wartosc')?.getAttribute('aria-required')).toBe(
      'true',
    );
  });

  it('sends a normalized input with the amount as a decimal string', async () => {
    const { element, host, submit } = await setup();
    fillRequired(element);
    type(element, '#zamowienie-klient-nip', '123-456-32-18');
    type(element, '#zamowienie-dostawcy', 'Dostawca A\n\n  Dostawca B  \n');
    type(element, '#zamowienie-komentarz', '   ');
    await submit();

    expect(host.submitted).toHaveLength(1);
    expect(host.submitted[0]).toEqual({
      orderDate: '2026-10-28',
      channel: 'phone',
      value: '1234.50',
      customer: {
        kind: 'person',
        name: 'Jan Testowy',
        phone: null,
        email: null,
        street: null,
        postalCode: null,
        city: null,
        taxId: '123-456-32-18',
      },
      fulfillmentStatus: 'inProgress',
      operational: {
        suppliers: ['Dostawca A', 'Dostawca B'],
        zoReferences: [],
        invoiceNumber: null,
        hasPhotos: false,
        hasReview: false,
        notes: null,
      },
      comment: null,
    });
    expect(summary(element)).toBeNull();
  });

  it('accepts 0 zł', async () => {
    const { element, host, submit } = await setup();
    fillRequired(element);
    type(element, '#zamowienie-wartosc', '0');
    await submit();

    expect(host.submitted[0].value).toBe('0.00');
  });

  it('validates in the UI, shows a summary with links and moves focus to it', async () => {
    const { element, host, submit } = await setup();
    type(element, '#zamowienie-wartosc', '1,234');
    type(element, '#zamowienie-klient-nip', '1234567890');
    type(element, '#zamowienie-klient-kod', '00001');
    await submit();

    expect(host.submitted).toHaveLength(0);
    const box = summary(element);
    expect(box).not.toBeNull();
    await vi.waitFor(() => expect(document.activeElement).toBe(box));
    const links = Array.from(box?.querySelectorAll('a') ?? []).map((link) => [
      link.getAttribute('href'),
      link.textContent?.trim(),
    ]);
    expect(links).toEqual([
      ['#zamowienie-klient-nazwa', 'Podaj imię i nazwisko lub nazwę firmy.'],
      ['#zamowienie-klient-kod', 'Podaj kod pocztowy w formacie 00-000.'],
      ['#zamowienie-klient-nip', 'Podaj poprawny NIP: 10 cyfr z prawidłową sumą kontrolną.'],
      ['#zamowienie-kanal', 'Wybierz kanał.'],
      [
        '#zamowienie-wartosc',
        'Podaj kwotę nieujemną z najwyżej dwoma miejscami po przecinku, np. 1 234,50.',
      ],
    ]);
    const value = element.querySelector('#zamowienie-wartosc') as HTMLInputElement;
    expect(value.getAttribute('aria-invalid')).toBe('true');
    expect(value.getAttribute('aria-describedby')).toContain('zamowienie-wartosc-blad');

    (box?.querySelector('a[href="#zamowienie-wartosc"]') as HTMLAnchorElement).click();
    expect(document.activeElement).toBe(value);
  });

  it.each(['-5', 'abc', '12345678901234567,00'])('rejects the amount %s', async (amount) => {
    const { element, host, submit } = await setup();
    fillRequired(element);
    type(element, '#zamowienie-wartosc', amount);
    await submit();

    expect(host.submitted).toHaveLength(0);
    expect(element.querySelector('#zamowienie-wartosc-blad')).not.toBeNull();
  });

  it('pins API errors to fields by their JSON keys and lists them in the summary', async () => {
    const error: ApiError = {
      kind: 'validation',
      detail: null,
      fieldErrors: {
        'customer.taxid': ['NIP jest niepoprawny.'],
        'operational.suppliers[1]': ['Za długa nazwa.'],
        unknown: ['Inny błąd.'],
      },
    };
    const { element, host, submit } = await setup((current) => (current.outcome = { error }));
    fillRequired(element);
    await submit();

    expect(host.submitted).toHaveLength(1);
    expect(element.querySelector('#zamowienie-klient-nip-blad')?.textContent).toContain(
      'NIP jest niepoprawny.',
    );
    expect(element.querySelector('#zamowienie-dostawcy-blad')?.textContent).toContain(
      'Pozycja 2: Za długa nazwa.',
    );
    const box = summary(element);
    expect(box?.textContent).toContain('NIP jest niepoprawny.');
    expect(box?.textContent).toContain('Inny błąd.');
    await vi.waitFor(() => expect(document.activeElement).toBe(box));
  });

  it('shows a general API problem in the summary and keeps the data', async () => {
    const error: ApiError = {
      kind: 'problem',
      status: 422,
      code: 'status_change_requires_action',
      detail: 'Status zmienia akcja.',
    };
    const { element, submit } = await setup((current) => (current.outcome = { error }));
    fillRequired(element);
    await submit();

    expect(summary(element)?.textContent).toContain('Status zmienia akcja.');
    expect((element.querySelector('#zamowienie-klient-nazwa') as HTMLInputElement).value).toBe(
      '  Jan Testowy ',
    );
  });

  it('shows the status of a cancelled order as read-only and sends „cancelled”', async () => {
    const { element, host, submit } = await setup((current) => {
      current.initial.set(orderFormFrom(orderDetails({ fulfillmentStatus: 'cancelled' })));
      current.cancelled.set(true);
    });

    expect(element.querySelector('#zamowienie-status')).toBeNull();
    expect(element.textContent).toContain('Anulowane');
    expect(element.textContent).toContain(
      'Status anulowanego zamówienia zmienia akcja „Przywróć zamówienie”.',
    );
    await submit();
    expect(host.submitted[0].fulfillmentStatus).toBe('cancelled');
  });

  it('shows shop-owned fields as read-only and leaves them out of the request', async () => {
    const { element, host, submit } = await setup((current) => {
      current.initial.set(
        orderFormFrom(orderDetails({ source: 'wooCommerce', channel: 'shop', externalId: '77' })),
      );
      current.shopOwned.set(['orderDate', 'value', 'channel', 'customer']);
    });

    for (const id of ['zamowienie-data', 'zamowienie-kanal', 'zamowienie-wartosc']) {
      expect(element.querySelector(`#${id}`)).toBeNull();
    }
    expect(element.querySelector('#zamowienie-klient-nazwa')).toBeNull();
    expect(element.textContent).toContain('Zamówienie pochodzi ze sklepu.');
    expect(element.textContent).toContain(formatPln('1234.50'));
    expect(element.textContent).toContain('28.10.2026');
    expect(element.textContent).toContain('Jan Testowy');

    type(element, '#zamowienie-faktura', 'FV/2/2026');
    await submit();
    const sent = host.submitted[0];
    expect(sent.orderDate).toBeUndefined();
    expect(sent.value).toBeUndefined();
    expect(sent.channel).toBeUndefined();
    expect(sent.customer).toBeUndefined();
    expect(sent.operational.invoiceNumber).toBe('FV/2/2026');
  });

  it('replaces the data and clears errors when the initial state changes', async () => {
    const { element, host, fixture, submit } = await setup();
    await submit();
    expect(summary(element)).not.toBeNull();

    host.initial.set(orderFormFrom(orderDetails()));
    await fixture.whenStable();

    expect(summary(element)).toBeNull();
    expect((element.querySelector('#zamowienie-klient-nazwa') as HTMLInputElement).value).toBe(
      'Jan Testowy',
    );
    expect((element.querySelector('#zamowienie-wartosc') as HTMLInputElement).value).toBe(
      '1234,50',
    );
    expect(element.querySelector('#zamowienie-klient-nazwa-blad')).toBeNull();
  });

  describe('anonymized customer (SF-22)', () => {
    const anonymizedCustomer = {
      kind: 'person' as const,
      name: '[zanonimizowano]',
      phone: null,
      email: null,
      street: null,
      postalCode: null,
      city: null,
      taxId: null,
    };

    it('shows the customer, also the kind, read-only and sends the stored customer', async () => {
      const { element, host, submit } = await setup((current) => {
        current.initial.set(orderFormFrom(orderDetails({ customer: anonymizedCustomer })));
        current.customerAnonymized.set(true);
      });

      expect(element.querySelector('#zamowienie-klient-nazwa')).toBeNull();
      expect(element.querySelector('#zamowienie-klient-rodzaj')).toBeNull();
      const note = element.querySelector('#zamowienie-klient-zanonimizowany');
      expect(note?.textContent).toContain('Dane klienta zanonimizowano; nie można ich zmienić.');
      expect(element.querySelector('fieldset')?.getAttribute('aria-describedby')).toBe(
        'zamowienie-klient-zanonimizowany',
      );
      expect(element.textContent).toContain('[zanonimizowano]');
      expect(element.textContent).not.toContain('Dane klienta pochodzą ze sklepu');

      await submit();
      expect(host.submitted).toHaveLength(1);
      expect(host.submitted[0].customer).toEqual(anonymizedCustomer);
    });

    it('shows the API rejection of a changed customer in the summary', async () => {
      const { element, host, submit } = await setup((current) => {
        current.initial.set(orderFormFrom(orderDetails({ customer: anonymizedCustomer })));
        current.customerAnonymized.set(true);
      });
      host.outcome = {
        error: {
          kind: 'problem',
          status: 422,
          code: 'order_customer_anonymized',
          detail: 'Dane klienta zanonimizowano. Nie można ich zmienić ani przywrócić.',
        },
      };

      await submit();

      expect(summary(element)?.textContent).toContain(
        'Dane klienta zanonimizowano. Nie można ich zmienić ani przywrócić.',
      );
    });

    it('keeps the shop note for a shop order that is not anonymized', async () => {
      const { element } = await setup((current) => {
        current.initial.set(orderFormFrom(orderDetails({ source: 'wooCommerce' })));
        current.shopOwned.set(['orderDate', 'value', 'channel', 'customer']);
      });

      expect(element.textContent).toContain('Dane klienta pochodzą ze sklepu');
      expect(element.querySelector('#zamowienie-klient-zanonimizowany')).toBeNull();
    });
  });
});
