import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  linkedSignal,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import {
  FieldTree,
  FormField,
  PathKind,
  SchemaPath,
  SchemaPathRules,
  TreeValidationResult,
  ValidationError,
  applyWhen,
  form,
  submit,
  validate,
} from '@angular/forms/signals';
import { ApiError } from '../../core/http/api-error';
import { formatDate, formatPln, parsePlnAmount } from '../../shared/format/pl-format';
import { Button } from '../../shared/ui/button';
import { Field } from '../../shared/ui/field';
import {
  LIMITS,
  ORDER_MESSAGES,
  OrderFormModel,
  amountError,
  isValidDate,
  isValidEmail,
  isValidPostalCode,
  isValidTaxId,
  listItems,
  orderErrorMessage,
  toOrderInput,
} from './order-form-model';
import {
  CHANNELS,
  CHANNEL_LABELS,
  CUSTOMER_KINDS,
  CUSTOMER_KIND_LABELS,
  EDITABLE_STATUSES,
  OrderChannel,
  OrderInput,
  STATUS_LABELS,
  ShopOwnedField,
} from './order-models';

/** Opis pola wartości przy edycji (SF-10): korekta wartości, zwrot i korekta sprzedaży. */
export const VALUE_CORRECTION_HINT =
  'Np. 1 234,50. Korekta wartości zmienia wartość zamówienia w okresie jego daty. Oddany towar ' +
  'bez zwrotu pieniędzy zapisz korektą sprzedaży, a zwrócone pieniądze zwrotem.';

/** Wynik zapisu przekazany formularzowi: `null`, gdy rodzic obsłużył wynik sam. */
export type OrderFormOutcome = null | { readonly error: ApiError };
export type OrderFormAction = (input: OrderInput) => Promise<OrderFormOutcome>;

interface SummaryItem {
  readonly id: string | null;
  readonly message: string;
}

interface RegisteredField {
  /** Ścieżka JSON w `ValidationProblemDetails` (`customer.name`, `operational.suppliers`). */
  readonly key: string;
  readonly id: string;
  readonly owner: ShopOwnedField | null;
  readonly field: () => FieldTree<string>;
}

type StringPath = SchemaPath<string, SchemaPathRules.Supported, PathKind>;

/** Reguła na wartości tekstowej: komunikat albo `null`. */
function rule(path: StringPath, check: (value: string) => string | null): void {
  validate(path, ({ value }) => {
    const message = check(value());
    return message ? { kind: 'order', message } : null;
  });
}

function maxLengthRule(path: StringPath, limit: number, message: string): void {
  rule(path, (value) => (value.trim().length > limit ? message : null));
}

function listRule(path: StringPath, itemLimit: number, tooMany: string, tooLong: string): void {
  rule(path, (value) => {
    const items = listItems(value);
    if (items.length > LIMITS.listItems) {
      return tooMany;
    }
    return items.some((item) => item.length > itemLimit) ? tooLong : null;
  });
}

/**
 * Wspólny formularz dodawania i edycji zamówienia (sekcje „Klient”, „Zamówienie”,
 * „Realizacja”, „Uwagi”). Pola należące do sklepu (`shopOwnedFields`), dane klienta po
 * anonimizacji (SF-22) i status anulowanego zamówienia są tylko do odczytu. Po nieudanym zapisie fokus trafia na podsumowanie błędów.
 */
@Component({
  selector: 'app-order-form',
  imports: [Button, Field, FormField],
  templateUrl: './order-form.html',
})
export class OrderForm {
  readonly initial = input.required<OrderFormModel>();
  readonly shopOwnedFields = input<readonly string[]>([]);
  readonly cancelled = input(false);
  readonly submitLabel = input('Zapisz');
  /** Edycja istniejącego zamówienia: pole wartości wyjaśnia różnicę korekty wartości (SF-10). */
  readonly editingExisting = input(false);
  /**
   * Dane klienta zanonimizowano (SF-22): pola klienta, także rodzaj, są tylko do odczytu, a zapis
   * wysyła zapisanego klienta bez zmian.
   */
  readonly customerAnonymized = input(false);
  readonly action = input.required<OrderFormAction>();

  protected readonly channels = CHANNELS;
  protected readonly channelLabels = CHANNEL_LABELS;
  protected readonly customerKinds = CUSTOMER_KINDS;
  protected readonly customerKindLabels = CUSTOMER_KIND_LABELS;
  protected readonly statuses = EDITABLE_STATUSES;
  protected readonly statusLabels = STATUS_LABELS;
  protected readonly cancelledLabel = STATUS_LABELS.cancelled;

  private readonly injector = inject(Injector);
  private readonly summaryElement = viewChild<ElementRef<HTMLElement>>('summaryBox');

  private readonly model = linkedSignal(() => structuredClone(this.initial()));
  protected readonly form = form(this.model, (path) => {
    applyWhen(
      path.orderDate,
      () => !this.owns('orderDate'),
      (date) => {
        rule(date, (value) =>
          value.trim() === '' || !isValidDate(value) ? ORDER_MESSAGES.orderDateRequired : null,
        );
      },
    );
    applyWhen(
      path.channel,
      () => !this.owns('channel'),
      (channel) => {
        validate(channel, ({ value }) =>
          value() === '' ? { kind: 'order', message: ORDER_MESSAGES.channelRequired } : null,
        );
      },
    );
    applyWhen(
      path.value,
      () => !this.owns('value'),
      (value) => rule(value, amountError),
    );
    applyWhen(
      path.customer,
      () => !this.owns('customer'),
      (customer) => {
        rule(customer.name, (value) => (value.trim() === '' ? ORDER_MESSAGES.nameRequired : null));
        maxLengthRule(customer.name, LIMITS.name, ORDER_MESSAGES.nameTooLong);
        maxLengthRule(customer.phone, LIMITS.phone, ORDER_MESSAGES.phoneTooLong);
        rule(customer.email, (value) => (isValidEmail(value) ? null : ORDER_MESSAGES.emailInvalid));
        maxLengthRule(customer.street, LIMITS.street, ORDER_MESSAGES.streetTooLong);
        rule(customer.postalCode, (value) =>
          isValidPostalCode(value) ? null : ORDER_MESSAGES.postalCodeInvalid,
        );
        maxLengthRule(customer.city, LIMITS.city, ORDER_MESSAGES.cityTooLong);
        rule(customer.taxId, (value) => (isValidTaxId(value) ? null : ORDER_MESSAGES.taxIdInvalid));
      },
    );
    listRule(
      path.operational.suppliers,
      LIMITS.supplier,
      ORDER_MESSAGES.suppliersTooMany,
      ORDER_MESSAGES.supplierTooLong,
    );
    listRule(
      path.operational.zoReferences,
      LIMITS.zoReference,
      ORDER_MESSAGES.zoTooMany,
      ORDER_MESSAGES.zoTooLong,
    );
    maxLengthRule(
      path.operational.invoiceNumber,
      LIMITS.invoiceNumber,
      ORDER_MESSAGES.invoiceTooLong,
    );
    maxLengthRule(path.operational.notes, LIMITS.notes, ORDER_MESSAGES.notesTooLong);
    maxLengthRule(path.comment, LIMITS.comment, ORDER_MESSAGES.commentTooLong);
  });

  /** Pola tekstowe z identyfikatorem kontrolki i kluczem błędu API. */
  private readonly registry: readonly RegisteredField[] = [
    this.register(
      'customer.kind',
      'zamowienie-klient-rodzaj',
      'customer',
      () => this.form.customer.kind as FieldTree<string>,
    ),
    this.register(
      'customer.name',
      'zamowienie-klient-nazwa',
      'customer',
      () => this.form.customer.name,
    ),
    this.register(
      'customer.phone',
      'zamowienie-klient-telefon',
      'customer',
      () => this.form.customer.phone,
    ),
    this.register(
      'customer.email',
      'zamowienie-klient-email',
      'customer',
      () => this.form.customer.email,
    ),
    this.register(
      'customer.street',
      'zamowienie-klient-ulica',
      'customer',
      () => this.form.customer.street,
    ),
    this.register(
      'customer.postalCode',
      'zamowienie-klient-kod',
      'customer',
      () => this.form.customer.postalCode,
    ),
    this.register(
      'customer.city',
      'zamowienie-klient-miasto',
      'customer',
      () => this.form.customer.city,
    ),
    this.register(
      'customer.taxId',
      'zamowienie-klient-nip',
      'customer',
      () => this.form.customer.taxId,
    ),
    this.register('orderDate', 'zamowienie-data', 'orderDate', () => this.form.orderDate),
    this.register(
      'channel',
      'zamowienie-kanal',
      'channel',
      () => this.form.channel as FieldTree<string>,
    ),
    this.register('value', 'zamowienie-wartosc', 'value', () => this.form.value),
    this.register(
      'fulfillmentStatus',
      'zamowienie-status',
      null,
      () => this.form.fulfillmentStatus as FieldTree<string>,
    ),
    this.register(
      'operational.suppliers',
      'zamowienie-dostawcy',
      null,
      () => this.form.operational.suppliers,
    ),
    this.register(
      'operational.zoReferences',
      'zamowienie-zo',
      null,
      () => this.form.operational.zoReferences,
    ),
    this.register(
      'operational.invoiceNumber',
      'zamowienie-faktura',
      null,
      () => this.form.operational.invoiceNumber,
    ),
    this.register('operational.notes', 'zamowienie-uwagi', null, () => this.form.operational.notes),
    this.register('comment', 'zamowienie-komentarz', null, () => this.form.comment),
  ];

  protected readonly busy = computed(() => this.form().submitting());
  protected readonly ownsCustomer = computed(() => this.owns('customer'));
  protected readonly customerReadOnly = computed(
    () => this.ownsCustomer() || this.customerAnonymized(),
  );
  protected readonly ownsOrderDate = computed(() => this.owns('orderDate'));
  protected readonly ownsChannel = computed(() => this.owns('channel'));
  protected readonly ownsValue = computed(() => this.owns('value'));
  protected readonly valueHint = computed(() =>
    this.editingExisting() ? VALUE_CORRECTION_HINT : 'Np. 1 234,50. Wartość 0 zł jest dozwolona.',
  );
  protected readonly hasShopFields = computed(() => this.shopOwnedFields().length > 0);

  /** Podsumowanie pokazuje się po nieudanej próbie zapisu i znika po poprawieniu błędów. */
  private readonly attempted = signal(false);
  private readonly formError = signal<string | null>(null);
  private readonly unmatchedErrors = signal<readonly string[]>([]);
  protected readonly summary = computed<readonly SummaryItem[]>(() => {
    if (!this.attempted()) {
      return [];
    }
    const items: SummaryItem[] = [];
    for (const entry of this.registry) {
      const message =
        entry.owner === null || !this.owns(entry.owner) ? this.errorOf(entry.field()) : null;
      if (message) {
        items.push({ id: entry.id, message });
      }
    }
    for (const message of this.unmatchedErrors()) {
      items.push({ id: null, message });
    }
    const general = this.formError();
    if (general) {
      items.push({ id: null, message: general });
    }
    return items;
  });

  constructor() {
    // Nowy stan początkowy (np. „Wczytaj aktualne dane”) czyści dotknięcia i podsumowanie.
    effect(() => {
      this.initial();
      untracked(() => {
        this.form().reset();
        this.attempted.set(false);
        this.formError.set(null);
        this.unmatchedErrors.set([]);
      });
    });
  }

  /** Błąd pola do wyświetlenia: po dotknięciu pola albo próbie zapisu. */
  protected errorOf<T>(field: FieldTree<T>): string | null {
    const state = field();
    if (!state.touched()) {
      return null;
    }
    return state.errors().find((error) => error.message)?.message ?? null;
  }

  protected shownValue(): string {
    return formatPln(parsePlnAmount(this.model().value));
  }

  protected shownDate(): string {
    return formatDate(this.model().orderDate);
  }

  protected shownChannel(): string {
    const channel = this.model().channel;
    return channel ? CHANNEL_LABELS[channel as OrderChannel] : '';
  }

  protected focusField(event: Event, id: string): void {
    event.preventDefault();
    document.getElementById(id)?.focus();
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    this.formError.set(null);
    this.unmatchedErrors.set([]);
    this.attempted.set(false);
    await submit(this.form, () => this.save());
    if (this.form().invalid() || this.formError() !== null || this.unmatchedErrors().length > 0) {
      this.attempted.set(true);
      afterNextRender(() => this.summaryElement()?.nativeElement.focus(), {
        injector: this.injector,
      });
    }
  }

  private async save(): Promise<TreeValidationResult> {
    const outcome = await this.action()(toOrderInput(this.model(), this.shopOwnedFields()));
    if (!outcome) {
      return null;
    }
    const { error } = outcome;
    if (error.kind !== 'validation') {
      this.formError.set(orderErrorMessage(error));
      return null;
    }
    const fieldErrors: ValidationError.WithFieldTree[] = [];
    const unmatched: string[] = [];
    for (const [key, messages] of Object.entries(error.fieldErrors)) {
      const { base, index } = splitKey(key);
      const entry = this.registry.find((candidate) => candidate.key.toLowerCase() === base);
      for (const message of messages) {
        const text = index === null ? message : `Pozycja ${index + 1}: ${message}`;
        if (entry && (entry.owner === null || !this.owns(entry.owner))) {
          fieldErrors.push({ kind: 'server', message: text, fieldTree: entry.field() });
        } else {
          unmatched.push(text);
        }
      }
    }
    this.unmatchedErrors.set(unmatched);
    if (fieldErrors.length === 0 && unmatched.length === 0) {
      this.formError.set(error.detail ?? ORDER_MESSAGES.saveFailed);
    }
    return fieldErrors;
  }

  private owns(field: ShopOwnedField): boolean {
    return this.shopOwnedFields().includes(field);
  }

  private register(
    key: string,
    id: string,
    owner: ShopOwnedField | null,
    field: () => FieldTree<string>,
  ): RegisteredField {
    return { key, id, owner, field };
  }
}

/** `operational.suppliers[0]` → `operational.suppliers` i indeks 0 (klucze są już małymi literami). */
function splitKey(key: string): { base: string; index: number | null } {
  const normalized = key.replace(/^\$\./, '');
  const match = /^(.*)\[(\d+)\]$/.exec(normalized);
  return match ? { base: match[1], index: Number(match[2]) } : { base: normalized, index: null };
}
