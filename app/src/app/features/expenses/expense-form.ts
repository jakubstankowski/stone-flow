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
  TreeValidationResult,
  ValidationError,
  form,
  submit,
  validate,
} from '@angular/forms/signals';
import { ApiError } from '../../core/http/api-error';
import { parseSignedPlnAmount, todayInWarsaw } from '../../shared/format/pl-format';
import { Button } from '../../shared/ui/button';
import { Field } from '../../shared/ui/field';
import {
  EXPENSE_LIMITS,
  EXPENSE_MESSAGES,
  ExpenseFormModel,
  NEGATIVE_AMOUNT_HINT,
  expenseErrorMessage,
  grossAmountError,
  isValidDate,
  netAmountError,
  optionalDateError,
  paidOnError,
  toExpenseInput,
} from './expense-form-model';
import {
  CATEGORIES,
  CATEGORY_LABELS,
  ExpenseInput,
  NEGATIVE_AMOUNT_LABEL,
  PAYMENT_METHODS,
  PAYMENT_METHOD_LABELS,
  isNegativeAmount,
} from './expense-models';

/** Wynik zapisu przekazany formularzowi: `null`, gdy rodzic obsłużył wynik sam. */
export type ExpenseFormOutcome = null | { readonly error: ApiError };
export type ExpenseFormAction = (input: ExpenseInput) => Promise<ExpenseFormOutcome>;

interface SummaryItem {
  readonly id: string | null;
  readonly message: string;
}

interface RegisteredField {
  /** Nazwa pola w `ValidationProblemDetails`. */
  readonly key: string;
  readonly id: string;
  readonly field: () => FieldTree<string>;
}

function message(text: string | null): ValidationError | null {
  return text ? { kind: 'expense', message: text } : null;
}

function maxLength(value: string, limit: number, text: string): string | null {
  return value.trim().length > limit ? text : null;
}

/**
 * Wspólny formularz dodawania i poprawy wydatku (sekcje „Wydatek”, „Kwoty”, „Daty”, „Płatność
 * i dokument”, „Komentarz”). Ujemna kwota brutto pokazuje opis „Korekta / zwrot od dostawcy”.
 * Wyczyszczenie daty zapłaty opłaconego wydatku wymaga potwierdzenia (`confirmClearPaid`) — tylko
 * w UI; API przyjmuje `paidOn = null` bez flagi.
 */
@Component({
  selector: 'app-expense-form',
  imports: [Button, Field, FormField],
  templateUrl: './expense-form.html',
})
export class ExpenseForm {
  readonly initial = input.required<ExpenseFormModel>();
  readonly submitLabel = input('Zapisz');
  readonly action = input.required<ExpenseFormAction>();
  /** Potwierdzenie usunięcia daty zapłaty; `null` pomija pytanie (np. nowy wydatek). */
  readonly confirmClearPaid = input<(() => Promise<boolean>) | null>(null);

  protected readonly categories = CATEGORIES;
  protected readonly categoryLabels = CATEGORY_LABELS;
  protected readonly methods = PAYMENT_METHODS;
  protected readonly methodLabels = PAYMENT_METHOD_LABELS;
  protected readonly negativeLabel = NEGATIVE_AMOUNT_LABEL;
  protected readonly negativeHint = NEGATIVE_AMOUNT_HINT;
  protected readonly today = todayInWarsaw();

  private readonly injector = inject(Injector);
  private readonly summaryElement = viewChild<ElementRef<HTMLElement>>('summaryBox');

  private readonly model = linkedSignal(() => structuredClone(this.initial()));
  protected readonly form = form(this.model, (path) => {
    validate(path.description, ({ value }) =>
      message(
        value().trim() === ''
          ? EXPENSE_MESSAGES.descriptionRequired
          : maxLength(value(), EXPENSE_LIMITS.description, EXPENSE_MESSAGES.descriptionTooLong),
      ),
    );
    validate(path.category, ({ value }) =>
      message(value() === '' ? EXPENSE_MESSAGES.categoryRequired : null),
    );
    validate(path.counterparty, ({ value }) =>
      message(
        maxLength(value(), EXPENSE_LIMITS.counterparty, EXPENSE_MESSAGES.counterpartyTooLong),
      ),
    );
    validate(path.costDate, ({ value }) =>
      message(
        value() === ''
          ? EXPENSE_MESSAGES.costDateRequired
          : isValidDate(value())
            ? null
            : EXPENSE_MESSAGES.dateInvalid,
      ),
    );
    validate(path.dueDate, ({ value }) => message(optionalDateError(value())));
    validate(path.paidOn, ({ value }) => message(paidOnError(value(), this.today)));
    validate(path.grossAmount, ({ value }) => message(grossAmountError(value())));
    validate(path.netAmount, ({ value, valueOf }) =>
      message(netAmountError(value(), valueOf(path.grossAmount))),
    );
    validate(path.documentReference, ({ value }) =>
      message(
        maxLength(
          value(),
          EXPENSE_LIMITS.documentReference,
          EXPENSE_MESSAGES.documentReferenceTooLong,
        ),
      ),
    );
    validate(path.payer, ({ value }) =>
      message(maxLength(value(), EXPENSE_LIMITS.payer, EXPENSE_MESSAGES.payerTooLong)),
    );
    validate(path.comment, ({ value }) =>
      message(maxLength(value(), EXPENSE_LIMITS.comment, EXPENSE_MESSAGES.commentTooLong)),
    );
  });

  private readonly registry: readonly RegisteredField[] = [
    { key: 'description', id: 'wydatek-opis', field: () => this.form.description },
    {
      key: 'category',
      id: 'wydatek-kategoria',
      field: () => this.form.category as FieldTree<string>,
    },
    { key: 'counterparty', id: 'wydatek-kontrahent', field: () => this.form.counterparty },
    { key: 'grossAmount', id: 'wydatek-brutto', field: () => this.form.grossAmount },
    { key: 'netAmount', id: 'wydatek-netto', field: () => this.form.netAmount },
    { key: 'costDate', id: 'wydatek-data-kosztu', field: () => this.form.costDate },
    { key: 'dueDate', id: 'wydatek-termin', field: () => this.form.dueDate },
    { key: 'paidOn', id: 'wydatek-data-zaplaty', field: () => this.form.paidOn },
    {
      key: 'paymentMethod',
      id: 'wydatek-metoda',
      field: () => this.form.paymentMethod as FieldTree<string>,
    },
    { key: 'payer', id: 'wydatek-platnik', field: () => this.form.payer },
    { key: 'documentReference', id: 'wydatek-dokument', field: () => this.form.documentReference },
    { key: 'comment', id: 'wydatek-komentarz', field: () => this.form.comment },
  ];

  protected readonly busy = computed(() => this.form().submitting());
  /** Brutto wpisane jako ujemne (tekstowo): pokazuje opis korekty / zwrotu. */
  protected readonly negative = computed(() => {
    const amount = parseSignedPlnAmount(this.model().grossAmount);
    return amount !== null && isNegativeAmount(amount);
  });

  private readonly attempted = signal(false);
  private readonly general = signal<readonly string[]>([]);
  protected readonly summary = computed<readonly SummaryItem[]>(() => {
    if (!this.attempted()) {
      return [];
    }
    const items: SummaryItem[] = [];
    for (const entry of this.registry) {
      const text = this.errorOf(entry.field());
      if (text) {
        items.push({ id: entry.id, message: text });
      }
    }
    for (const text of this.general()) {
      items.push({ id: null, message: text });
    }
    return items;
  });

  constructor() {
    // Nowy stan początkowy (np. „Wczytaj ponownie”) czyści dotknięcia i podsumowanie.
    effect(() => {
      this.initial();
      untracked(() => {
        this.form().reset();
        this.attempted.set(false);
        this.general.set([]);
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

  protected focusField(event: Event, id: string): void {
    event.preventDefault();
    document.getElementById(id)?.focus();
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    this.general.set([]);
    this.attempted.set(false);
    await submit(this.form, () => this.save());
    if (this.form().invalid() || this.general().length > 0) {
      this.attempted.set(true);
      afterNextRender(() => this.summaryElement()?.nativeElement.focus(), {
        injector: this.injector,
      });
    }
  }

  private async save(): Promise<TreeValidationResult> {
    const clearsPaid = this.initial().paidOn !== '' && this.model().paidOn === '';
    const confirm = this.confirmClearPaid();
    if (clearsPaid && confirm && !(await confirm())) {
      return null;
    }
    const outcome = await this.action()(toExpenseInput(this.model()));
    if (!outcome) {
      return null;
    }
    const { error } = outcome;
    if (error.kind !== 'validation') {
      this.general.set([expenseErrorMessage(error)]);
      return null;
    }
    const fieldErrors: ValidationError.WithFieldTree[] = [];
    const unmatched: string[] = [];
    for (const [key, messages] of Object.entries(error.fieldErrors)) {
      const normalized = key.replace(/^\$\./, '');
      const entry = this.registry.find((candidate) => candidate.key.toLowerCase() === normalized);
      for (const text of messages) {
        if (entry) {
          fieldErrors.push({ kind: 'server', message: text, fieldTree: entry.field() });
        } else {
          unmatched.push(text);
        }
      }
    }
    if (fieldErrors.length === 0 && unmatched.length === 0) {
      unmatched.push(error.detail ?? EXPENSE_MESSAGES.saveFailed);
    }
    this.general.set(unmatched);
    return fieldErrors;
  }
}
