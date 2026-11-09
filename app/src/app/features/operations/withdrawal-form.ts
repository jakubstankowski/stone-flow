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
  output,
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
import { todayInWarsaw } from '../../shared/format/pl-format';
import { Button } from '../../shared/ui/button';
import { Field } from '../../shared/ui/field';
import {
  WITHDRAWAL_MESSAGES,
  WithdrawalFormModel,
  amountError,
  methodError,
  personError,
  toWithdrawalInput,
  withdrawalErrorMessage,
  withdrawnOnError,
} from './withdrawal-form-model';
import { WITHDRAWAL_METHODS, WITHDRAWAL_METHOD_LABELS, WithdrawalInput } from './withdrawal-models';

/** Wynik zapisu przekazany formularzowi: `null`, gdy rodzic obsłużył wynik sam. */
export type WithdrawalFormOutcome = null | { readonly error: ApiError };
export type WithdrawalFormAction = (input: WithdrawalInput) => Promise<WithdrawalFormOutcome>;

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
  return text ? { kind: 'withdrawal', message: text } : null;
}

/**
 * Wspólny formularz dodania i poprawy wypłaty właściciela: osoba z podpowiedziami (`datalist`),
 * kwota, data wypłaty i metoda bez wartości domyślnej. Błędy API trafiają do pól po kluczu,
 * pozostałe do podsumowania.
 */
@Component({
  selector: 'app-withdrawal-form',
  imports: [Button, Field, FormField],
  templateUrl: './withdrawal-form.html',
})
export class WithdrawalForm {
  readonly initial = input.required<WithdrawalFormModel>();
  readonly action = input.required<WithdrawalFormAction>();
  readonly submitLabel = input('Zapisz');
  readonly cancelLabel = input('Anuluj');
  /** Podpowiedzi osób z wcześniejszych wypłat. */
  readonly people = input<readonly string[]>([]);

  readonly cancelled = output<void>();

  protected readonly methods = WITHDRAWAL_METHODS;
  protected readonly methodLabels = WITHDRAWAL_METHOD_LABELS;
  protected readonly today = todayInWarsaw();

  private readonly injector = inject(Injector);
  private readonly summaryElement = viewChild<ElementRef<HTMLElement>>('summaryBox');

  private readonly model = linkedSignal(() => structuredClone(this.initial()));
  protected readonly form = form(this.model, (path) => {
    validate(path.person, ({ value }) => message(personError(value())));
    validate(path.amount, ({ value }) => message(amountError(value())));
    validate(path.withdrawnOn, ({ value }) => message(withdrawnOnError(value(), this.today)));
    validate(path.method, ({ value }) => message(methodError(value())));
  });

  private readonly registry: readonly RegisteredField[] = [
    { key: 'person', id: 'wyplata-osoba', field: () => this.form.person },
    { key: 'amount', id: 'wyplata-kwota', field: () => this.form.amount },
    { key: 'withdrawnOn', id: 'wyplata-data', field: () => this.form.withdrawnOn },
    {
      key: 'method',
      id: 'wyplata-metoda',
      field: () => this.form.method as FieldTree<string>,
    },
  ];

  protected readonly busy = computed(() => this.form().submitting());

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
    // Nowy stan początkowy (np. „Dodaj kolejną wypłatę”) czyści dotknięcia i podsumowanie.
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
      afterNextRender(() => this.focusFirstError(), { injector: this.injector });
    }
  }

  /** Fokus na pierwszym błędnym polu; bez błędu pola — na podsumowaniu. */
  private focusFirstError(): void {
    const first = this.summary().find((item) => item.id !== null);
    const target = first?.id ? document.getElementById(first.id) : null;
    (target ?? this.summaryElement()?.nativeElement)?.focus();
  }

  private async save(): Promise<TreeValidationResult> {
    const outcome = await this.action()(toWithdrawalInput(this.model()));
    if (!outcome) {
      return null;
    }
    const { error } = outcome;
    if (error.kind !== 'validation') {
      this.general.set([withdrawalErrorMessage(error, WITHDRAWAL_MESSAGES.saveFailed)]);
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
      unmatched.push(error.detail ?? WITHDRAWAL_MESSAGES.saveFailed);
    }
    this.general.set(unmatched);
    return fieldErrors;
  }
}
