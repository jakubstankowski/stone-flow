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
  disabled,
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
  netFromGross,
  optionalDateError,
  paidOnError,
  toExpenseInput,
} from './expense-form-model';
import {
  ExpenseEditorModel,
  RECURRING_HINTS,
  RecurringFormOptions,
  dayOfMonthError,
  emptySchedule,
  endsOnError,
  previewQueryOf,
  recurringErrorMessage,
  recurringGrossError,
  recurringNetError,
  startsOnError,
  toRecurringExpenseInput,
} from './recurring-expense-form-model';
import { RecurringPreviewQuery } from './recurring-expense-models';
import { RecurringExpensePreviewPanel } from './recurring-expense-preview';
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
  /** Pole tylko jednego trybu (jednorazowy albo definicja). */
  readonly only?: 'expense' | 'definition';
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
 *
 * Z `recurring` formularz ma tryb definicji (SF-16): „Powtarzaj co miesiąc” (dodanie) albo stały
 * tryb (edycja). Tryb definicji ukrywa „Daty” i „Referencję dokumentu”, pokazuje „Harmonogram”
 * z podglądem i zapisuje przez `recurring.action`. Odznaczenie opcji przywraca wpisane daty.
 */
@Component({
  selector: 'app-expense-form',
  imports: [Button, Field, FormField, RecurringExpensePreviewPanel],
  templateUrl: './expense-form.html',
})
export class ExpenseForm {
  readonly initial = input.required<ExpenseFormModel>();
  readonly submitLabel = input('Zapisz');
  /** Zapis wydatku jednorazowego; bez niego formularz działa tylko w trybie definicji. */
  readonly action = input<ExpenseFormAction | null>(null);
  /** Tryb definicji wydatku cyklicznego; `null` — zwykły wydatek. */
  readonly recurring = input<RecurringFormOptions | null>(null);
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

  protected readonly hints = RECURRING_HINTS;

  private readonly model = linkedSignal<ExpenseEditorModel>(() => {
    const options = this.recurring();
    return {
      ...structuredClone(this.initial()),
      ...(options ? { ...options.schedule } : emptySchedule()),
      recurring: options ? options.kind === 'edit' || options.checked : false,
    };
  });
  protected readonly form = form(this.model, (path) => {
    // Wstrzymana definicja: dzień zablokowany, ale wysyłany z modelu (NC6).
    disabled(path.dayOfMonth, () => this.recurring()?.paused ?? false);
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
    validate(path.costDate, ({ value, valueOf }) =>
      message(
        valueOf(path.recurring)
          ? null
          : value() === ''
            ? EXPENSE_MESSAGES.costDateRequired
            : isValidDate(value())
              ? null
              : EXPENSE_MESSAGES.dateInvalid,
      ),
    );
    validate(path.dueDate, ({ value, valueOf }) =>
      message(valueOf(path.recurring) ? null : optionalDateError(value())),
    );
    validate(path.paidOn, ({ value, valueOf }) =>
      message(valueOf(path.recurring) ? null : paidOnError(value(), this.today)),
    );
    validate(path.grossAmount, ({ value, valueOf }) =>
      message(valueOf(path.recurring) ? recurringGrossError(value()) : grossAmountError(value())),
    );
    validate(path.netAmount, ({ value, valueOf }) =>
      message(
        valueOf(path.recurring)
          ? recurringNetError(value())
          : netAmountError(value(), valueOf(path.grossAmount)),
      ),
    );
    validate(path.dayOfMonth, ({ value, valueOf }) =>
      message(valueOf(path.recurring) ? dayOfMonthError(value()) : null),
    );
    validate(path.startsOn, ({ value, valueOf }) =>
      message(valueOf(path.recurring) ? startsOnError(value()) : null),
    );
    validate(path.endsOn, ({ value, valueOf }) =>
      message(valueOf(path.recurring) ? endsOnError(value(), valueOf(path.startsOn)) : null),
    );
    validate(path.documentReference, ({ value, valueOf }) =>
      message(
        valueOf(path.recurring)
          ? null
          : maxLength(
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
    {
      key: 'costDate',
      id: 'wydatek-data-kosztu',
      field: () => this.form.costDate,
      only: 'expense',
    },
    { key: 'dueDate', id: 'wydatek-termin', field: () => this.form.dueDate, only: 'expense' },
    {
      key: 'paidOn',
      id: 'wydatek-data-zaplaty',
      field: () => this.form.paidOn,
      only: 'expense',
    },
    {
      key: 'dayOfMonth',
      id: 'wydatek-dzien',
      field: () => this.form.dayOfMonth,
      only: 'definition',
    },
    {
      key: 'startsOn',
      id: 'wydatek-poczatek',
      field: () => this.form.startsOn,
      only: 'definition',
    },
    { key: 'endsOn', id: 'wydatek-koniec', field: () => this.form.endsOn, only: 'definition' },
    {
      key: 'paymentMethod',
      id: 'wydatek-metoda',
      field: () => this.form.paymentMethod as FieldTree<string>,
    },
    { key: 'payer', id: 'wydatek-platnik', field: () => this.form.payer },
    {
      key: 'documentReference',
      id: 'wydatek-dokument',
      field: () => this.form.documentReference,
      only: 'expense',
    },
    { key: 'comment', id: 'wydatek-komentarz', field: () => this.form.comment },
  ];

  protected readonly busy = computed(() => this.form().submitting());
  /** Tryb definicji: zaznaczone „Powtarzaj co miesiąc” albo edycja definicji. */
  protected readonly definitionMode = computed(() => this.model().recurring);
  protected readonly showRecurringToggle = computed(() => this.recurring()?.kind === 'create');
  protected readonly editingDefinition = computed(() => this.recurring()?.kind === 'edit');
  protected readonly pausedDefinition = computed(() => this.recurring()?.paused ?? false);
  protected readonly dayHint = computed(() => {
    if (this.pausedDefinition()) {
      return `${RECURRING_HINTS.dayPaused} ${RECURRING_HINTS.day}`;
    }
    return this.editingDefinition()
      ? `${RECURRING_HINTS.day} ${RECURRING_HINTS.dayEdit}`
      : RECURRING_HINTS.day;
  });
  /** Zapytanie podglądu (tylko przy poprawnym formacie harmonogramu). */
  protected readonly previewQuery = computed<RecurringPreviewQuery | null>(
    () => {
      const model = this.model();
      return model.recurring ? previewQueryOf(model, this.recurring()?.definitionId ?? null) : null;
    },
    { equal: (a, b) => JSON.stringify(a) === JSON.stringify(b) },
  );
  /** Brutto wpisane jako ujemne (tekstowo): pokazuje opis korekty / zwrotu. */
  protected readonly negative = computed(() => {
    if (this.model().recurring) {
      return false;
    }
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
    const mode = this.model().recurring ? 'definition' : 'expense';
    for (const entry of this.registry) {
      if (entry.only && entry.only !== mode) {
        continue;
      }
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
      this.recurring();
      untracked(() => {
        this.form().reset();
        this.attempted.set(false);
        this.general.set([]);
      });
    });
    // Netto wyliczane z brutto (÷ 1,23), dopóki użytkownik nie wpisał własnego netto.
    effect(() => {
      const suggestion = this.netSuggestion();
      untracked(() => {
        const net = this.model().netAmount;
        const previous = this.lastNetSuggestion;
        this.lastNetSuggestion = suggestion;
        if (suggestion !== null && (net.trim() === '' || net === previous)) {
          this.form.netAmount().value.set(suggestion);
        }
      });
    });
  }

  private readonly netSuggestion = computed(() => netFromGross(this.model().grossAmount));
  private lastNetSuggestion: string | null = null;

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
    await this.resubmit();
  }

  /** Czy formularz właśnie zapisuje (blokuje ponowienia z zewnątrz, np. „Spróbuj ponownie”). */
  readonly saving = computed(() => this.busy());

  /**
   * Zapis bieżących danych formularza tą samą ścieżką co „Zapisz” (walidacja, `busy`, błędy przy
   * polach). Używa go „Spróbuj ponownie” po `503`, żeby nie wysłać starszej wersji danych.
   */
  async resubmit(): Promise<void> {
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
    const model = this.model();
    const recurring = this.recurring();
    const definition = model.recurring && recurring !== null;
    let outcome: ExpenseFormOutcome;
    if (model.recurring && recurring) {
      outcome = await recurring.action(toRecurringExpenseInput(model));
    } else {
      const action = this.action();
      if (!action) {
        return null;
      }
      const clearsPaid = this.initial().paidOn !== '' && model.paidOn === '';
      const confirm = this.confirmClearPaid();
      if (clearsPaid && confirm && !(await confirm())) {
        return null;
      }
      outcome = await action(toExpenseInput(model));
    }
    if (!outcome) {
      return null;
    }
    const { error } = outcome;
    if (error.kind !== 'validation') {
      this.general.set([definition ? recurringErrorMessage(error) : expenseErrorMessage(error)]);
      return null;
    }
    const mode = definition ? 'definition' : 'expense';
    const fieldErrors: ValidationError.WithFieldTree[] = [];
    const unmatched: string[] = [];
    for (const [key, messages] of Object.entries(error.fieldErrors)) {
      const normalized = key.replace(/^\$\./, '');
      const entry = this.registry.find(
        (candidate) =>
          candidate.key.toLowerCase() === normalized &&
          (!candidate.only || candidate.only === mode),
      );
      for (const text of messages) {
        if (entry) {
          fieldErrors.push({ kind: 'server', message: text, fieldTree: entry.field() });
        } else {
          unmatched.push(text);
        }
      }
    }
    if (fieldErrors.length === 0 && unmatched.length === 0) {
      unmatched.push(
        error.detail ?? (definition ? recurringErrorMessage(error) : EXPENSE_MESSAGES.saveFailed),
      );
    }
    this.general.set(unmatched);
    return fieldErrors;
  }
}
