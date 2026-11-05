import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { formatDate, formatPln } from '../../shared/format/pl-format';
import { Button } from '../../shared/ui/button';
import { ConfirmDialog } from '../../shared/ui/confirm-dialog';
import { StatusBadge } from '../../shared/ui/status-badge';
import {
  EntryFormOutcome,
  PaymentFormModel,
  RefundFormModel,
  emptyPaymentForm,
  emptyRefundForm,
  entryErrorMessage,
  paymentFormFrom,
  refundFormFrom,
} from './entry-form-model';
import {
  OrderDetails,
  PAYMENT_METHOD_LABELS,
  PAYMENT_STATE_LABELS,
  PAYMENT_STATE_TONES,
  PaymentEntry,
  PaymentInput,
  REFUND_KIND_LABELS,
  REFUND_METHOD_LABELS,
  RefundEntry,
  RefundInput,
} from './order-models';
import { EntryInputs, EntryType } from './orders-api';
import { EntryOutcome, OrdersService } from './orders.service';
import { PaymentForm } from './payment-form';
import { RefundForm } from './refund-form';

export const ENTRY_CONFLICT_MESSAGE = 'Wpis został zmieniony w międzyczasie.';
export const ENTRY_NOT_FOUND_MESSAGE = 'Nie znaleziono wpisu. Mógł zostać usunięty w międzyczasie.';
export const DELETE_FAILED_MESSAGE = 'Nie udało się usunąć wpisu.';
export const ENTRY_KEY_REUSED_MESSAGE =
  'Ten formularz zapisał już wpis. Pokazano aktualne wpisy zamówienia.';

/** Otwarty formularz wpisu; naraz otwarty jest najwyżej jeden. */
type OpenForm =
  | {
      readonly type: 'payment';
      readonly entry: PaymentEntry | null;
      readonly initial: PaymentFormModel;
      readonly key: string;
    }
  | {
      readonly type: 'refund';
      readonly entry: RefundEntry | null;
      readonly initial: RefundFormModel;
      readonly key: string;
    };

type PendingDelete =
  | { readonly type: 'payment'; readonly entry: PaymentEntry }
  | { readonly type: 'refund'; readonly entry: RefundEntry };

interface Alert {
  readonly text: string;
  /** Pokazuje akcję „Wczytaj aktualne dane”. */
  readonly reload: boolean;
}

const NOTICES: Readonly<Record<EntryType, { saved: string; deleted: string }>> = {
  payment: { saved: 'Wpłata zapisana.', deleted: 'Wpłata usunięta.' },
  refund: { saved: 'Zwrot zapisany.', deleted: 'Zwrot usunięty.' },
};

/**
 * Sekcja „Płatności” w szczegółach zamówienia (SF-9): podsumowanie z backendu (Zapłacono,
 * stan, Do zapłaty, ostrzeżenia), tabele wpłat i zwrotów, formularze dodania i poprawy oraz
 * usuwanie po potwierdzeniu. Po każdym zapisie strona pobiera szczegóły ponownie (`reload`).
 * Gdy edycja zamówienia jest otwarta (`locked`), akcje wpisów są wyłączone.
 */
@Component({
  selector: 'app-order-payments',
  imports: [Button, ConfirmDialog, PaymentForm, RefundForm, StatusBadge],
  templateUrl: './order-payments.html',
})
export class OrderPayments {
  readonly order = input.required<OrderDetails>();
  readonly locked = input(false);
  /**
   * Ponowne pobranie szczegółów zamówienia przez stronę. Zwraca wczytane zamówienie (wejście
   * `order` odświeża się dopiero przy kolejnym renderowaniu) albo `null`, gdy się nie udało.
   */
  readonly reload = input.required<() => Promise<OrderDetails | null>>();
  /** Otwarcie i zamknięcie formularza wpisu (strona blokuje wtedy „Edytuj”). */
  readonly formOpenChange = output<boolean>();

  private readonly orders = inject(OrdersService);
  private readonly injector = inject(Injector);

  protected readonly openForm = signal<OpenForm | null>(null);
  protected readonly pendingDelete = signal<PendingDelete | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly alert = signal<Alert | null>(null);
  protected readonly reloading = signal(false);
  protected readonly deletingId = signal<string | null>(null);

  protected readonly cancelled = computed(() => this.order().fulfillmentStatus === 'cancelled');
  /** Akcje wpisów są niedostępne podczas edycji zamówienia, formularza wpisu i usuwania. */
  protected readonly actionsDisabled = computed(
    () => this.locked() || this.openForm() !== null || this.deletingId() !== null,
  );

  protected readonly stateLabels = PAYMENT_STATE_LABELS;
  protected readonly stateTones = PAYMENT_STATE_TONES;
  protected readonly paymentMethodLabels = PAYMENT_METHOD_LABELS;
  protected readonly refundMethodLabels = REFUND_METHOD_LABELS;
  protected readonly refundKindLabels = REFUND_KIND_LABELS;
  protected readonly formatDate = formatDate;
  protected readonly formatPln = formatPln;

  private readonly deleteDialog = viewChild<ConfirmDialog>('deleteDialog');
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private returnFocusTo: HTMLElement | null = null;

  protected readonly savePayment = (input: PaymentInput): Promise<EntryFormOutcome> =>
    this.save('payment', input);
  protected readonly saveRefund = (input: RefundInput): Promise<EntryFormOutcome> =>
    this.save('refund', input);

  protected addPayment(): void {
    this.open({ type: 'payment', entry: null, initial: emptyPaymentForm(), key: newKey() });
  }

  protected addRefund(): void {
    this.open({ type: 'refund', entry: null, initial: emptyRefundForm(), key: newKey() });
  }

  protected editPayment(entry: PaymentEntry): void {
    this.open({ type: 'payment', entry, initial: paymentFormFrom(entry), key: newKey() });
  }

  protected editRefund(entry: RefundEntry): void {
    this.open({ type: 'refund', entry, initial: refundFormFrom(entry), key: newKey() });
  }

  protected closeForm(): void {
    this.openForm.set(null);
    this.alert.set(null);
    this.formOpenChange.emit(false);
    const target = this.returnFocusTo;
    this.returnFocusTo = null;
    afterNextRender(
      () => {
        if (target?.isConnected && !(target as HTMLButtonElement).disabled) {
          target.focus();
        } else {
          this.heading()?.nativeElement.focus();
        }
      },
      { injector: this.injector },
    );
  }

  /** Po konflikcie: stan z serwera; otwarta poprawa dostaje aktualne dane wpisu. */
  protected async reloadCurrent(): Promise<void> {
    this.reloading.set(true);
    try {
      const fresh = await this.reload()();
      if (!fresh) {
        return;
      }
      this.alert.set(null);
      const current = this.openForm();
      if (current?.entry) {
        this.refreshOpenEntry(current, fresh);
      }
    } finally {
      this.reloading.set(false);
    }
  }

  protected async deletePayment(entry: PaymentEntry): Promise<void> {
    await this.delete({ type: 'payment', entry });
  }

  protected async deleteRefund(entry: RefundEntry): Promise<void> {
    await this.delete({ type: 'refund', entry });
  }

  protected formHeading(form: OpenForm): string {
    if (form.type === 'payment') {
      return form.entry ? 'Popraw wpłatę' : 'Dodaj wpłatę';
    }
    return form.entry ? 'Popraw zwrot' : 'Dodaj zwrot';
  }

  protected deleteHeading(pending: PendingDelete): string {
    return pending.type === 'payment' ? 'Usunąć wpłatę?' : 'Usunąć zwrot?';
  }

  /** Wskazanie wpisu w dialogu usuwania: rodzaj, kwota, data i metoda. */
  protected deleteSubject(pending: PendingDelete): string {
    const number = this.order().number;
    if (pending.type === 'payment') {
      const { amount, date, method } = pending.entry;
      return (
        `Wpłata ${formatPln(amount)} z ${formatDate(date)} (${PAYMENT_METHOD_LABELS[method]}) ` +
        `zostanie usunięta z zamówienia ${number}.`
      );
    }
    const { kind, amount, date, method } = pending.entry;
    return (
      `${REFUND_KIND_LABELS[kind]} ${formatPln(amount)} z ${formatDate(date)} ` +
      `(${REFUND_METHOD_LABELS[method]}) zostanie usunięty z zamówienia ${number}.`
    );
  }

  private open(form: OpenForm): void {
    if (this.actionsDisabled()) {
      return;
    }
    const active = document.activeElement;
    this.returnFocusTo = active instanceof HTMLElement ? active : null;
    this.notice.set(null);
    this.alert.set(null);
    this.openForm.set(form);
    this.formOpenChange.emit(true);
  }

  private refreshOpenEntry(current: OpenForm, order: OrderDetails): void {
    if (current.type === 'payment') {
      const fresh = order.payments.find((entry) => entry.id === current.entry?.id);
      if (fresh) {
        this.openForm.set({ ...current, entry: fresh, initial: paymentFormFrom(fresh) });
        return;
      }
    } else {
      const fresh = order.refunds.find((entry) => entry.id === current.entry?.id);
      if (fresh) {
        this.openForm.set({ ...current, entry: fresh, initial: refundFormFrom(fresh) });
        return;
      }
    }
    this.closeForm();
  }

  private async save<T extends EntryType>(
    type: T,
    input: EntryInputs[T],
  ): Promise<EntryFormOutcome> {
    const current = this.openForm();
    if (!current || current.type !== type) {
      return null;
    }
    const orderId = this.order().id;
    this.alert.set(null);
    const outcome: EntryOutcome = current.entry
      ? await this.orders.updateEntry(type, orderId, current.entry.id, input, current.entry.version)
      : await this.orders.addEntry(type, orderId, input, current.key);

    switch (outcome.kind) {
      case 'saved':
        this.closeForm();
        await this.reload()();
        this.notice.set(NOTICES[type].saved);
        return null;
      case 'keyReused':
        this.closeForm();
        await this.reload()();
        this.alert.set({ text: ENTRY_KEY_REUSED_MESSAGE, reload: false });
        return null;
      case 'conflict':
        this.alert.set({ text: ENTRY_CONFLICT_MESSAGE, reload: true });
        return null;
      case 'entryNotFound':
        this.alert.set({ text: ENTRY_NOT_FOUND_MESSAGE, reload: true });
        return null;
      case 'orderNotFound':
        this.closeForm();
        await this.reload()();
        return null;
      case 'rejected':
        return { message: outcome.message };
      case 'error':
        return { error: outcome.error };
    }
  }

  private async delete(pending: PendingDelete): Promise<void> {
    if (this.actionsDisabled()) {
      return;
    }
    this.pendingDelete.set(pending);
    if (!(await this.deleteDialog()?.open())) {
      return;
    }
    const { type, entry } = pending;
    this.notice.set(null);
    this.alert.set(null);
    this.deletingId.set(entry.id);
    try {
      const outcome = await this.orders.deleteEntry(type, this.order().id, entry.id, entry.version);
      switch (outcome.kind) {
        case 'saved':
          await this.reload()();
          this.notice.set(NOTICES[type].deleted);
          return;
        case 'conflict':
          this.alert.set({ text: ENTRY_CONFLICT_MESSAGE, reload: true });
          return;
        case 'entryNotFound':
          this.alert.set({ text: ENTRY_NOT_FOUND_MESSAGE, reload: true });
          return;
        case 'orderNotFound':
          await this.reload()();
          return;
        case 'rejected':
          this.alert.set({ text: outcome.message, reload: false });
          return;
        case 'error':
          this.alert.set({
            text: entryErrorMessage(outcome.error, DELETE_FAILED_MESSAGE),
            reload: false,
          });
          return;
        case 'keyReused':
          // Usuwanie nie używa klucza idempotencji; odpowiedź traktujemy jak nieudaną.
          this.alert.set({ text: DELETE_FAILED_MESSAGE, reload: true });
          return;
      }
    } finally {
      this.deletingId.set(null);
    }
  }
}

function newKey(): string {
  return crypto.randomUUID();
}
