import {
  Component,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
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
  SalesCorrectionFormModel,
  emptyPaymentForm,
  emptyRefundForm,
  emptySalesCorrectionForm,
  entryErrorMessage,
  paymentFormFrom,
  refundFormFrom,
  salesCorrectionFormFrom,
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
  SalesCorrectionEntry,
  SalesCorrectionInput,
} from './order-models';
import { EntryInputs, EntryType } from './orders-api';
import { EntryOutcome, OrdersService } from './orders.service';
import { PaymentForm } from './payment-form';
import { RefundForm } from './refund-form';
import { SalesCorrectionForm } from './sales-correction-form';
import { SalesCorrections } from './sales-corrections';

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
      /** Klucz idempotencji tylko przy dodaniu; poprawa używa `version`. */
      readonly key: string | null;
    }
  | {
      readonly type: 'refund';
      readonly entry: RefundEntry | null;
      readonly initial: RefundFormModel;
      readonly key: string | null;
    }
  | {
      readonly type: 'salesCorrection';
      readonly entry: SalesCorrectionEntry | null;
      readonly initial: SalesCorrectionFormModel;
      readonly key: string | null;
    };

type PendingDelete =
  | { readonly type: 'payment'; readonly entry: PaymentEntry }
  | { readonly type: 'refund'; readonly entry: RefundEntry }
  | { readonly type: 'salesCorrection'; readonly entry: SalesCorrectionEntry };

/** Sekcja szczegółów, w której pokazuje się komunikat: „Płatności” albo „Korekty sprzedaży”. */
export type EntrySection = 'payments' | 'corrections';

interface Notice {
  readonly section: EntrySection;
  readonly text: string;
}

interface Alert {
  readonly section: EntrySection;
  readonly text: string;
  /** Pokazuje akcję „Wczytaj aktualne dane”. */
  readonly reload: boolean;
}

const NOTICES: Readonly<Record<EntryType, { saved: string; deleted: string }>> = {
  payment: { saved: 'Wpłata zapisana.', deleted: 'Wpłata usunięta.' },
  refund: { saved: 'Zwrot zapisany.', deleted: 'Zwrot usunięty.' },
  salesCorrection: { saved: 'Korekta sprzedaży zapisana.', deleted: 'Korekta sprzedaży usunięta.' },
};

/** Nagłówki sekcji (`tabindex="-1"`) jako cel fokusu, gdy przycisk akcji zniknął. */
const SECTION_HEADINGS: Readonly<Record<EntrySection, string>> = {
  payments: 'platnosci-naglowek',
  corrections: 'korekty-naglowek',
};

function sectionOf(type: EntryType): EntrySection {
  return type === 'salesCorrection' ? 'corrections' : 'payments';
}

/**
 * Wpisy zamówienia w szczegółach: sekcja „Płatności” (SF-9: podsumowanie z backendu, tabele
 * wpłat i zwrotów) oraz pod nią sekcja „Korekty sprzedaży” (SF-10, `app-sales-corrections`).
 * Jeden stan dla obu sekcji: naraz otwarty jest jeden formularz wpisu, a licznik operacji
 * (`activeChange`) obejmuje wszystkie rodzaje wpisów, więc „Edytuj” jest zablokowane przy każdej
 * pracy z wpisem. Po każdym zapisie strona pobiera szczegóły ponownie (`reload`). Gdy edycja
 * zamówienia jest otwarta (`locked`), akcje wpisów są wyłączone.
 */
@Component({
  selector: 'app-order-payments',
  imports: [
    Button,
    ConfirmDialog,
    PaymentForm,
    RefundForm,
    SalesCorrectionForm,
    SalesCorrections,
    StatusBadge,
  ],
  templateUrl: './order-payments.html',
  host: { class: 'flex min-w-0 flex-col gap-6' },
})
export class OrderPayments {
  readonly order = input.required<OrderDetails>();
  readonly locked = input(false);
  /**
   * Ponowne pobranie szczegółów zamówienia przez stronę. Zwraca wczytane zamówienie (wejście
   * `order` odświeża się dopiero przy kolejnym renderowaniu) albo `null`, gdy się nie udało.
   */
  readonly reload = input.required<() => Promise<OrderDetails | null>>();
  /**
   * Praca z wpisem trwa (otwarty formularz, dialog usuwania, żądanie albo odświeżenie po nim).
   * Strona blokuje wtedy „Edytuj”, żeby odświeżenie nie trafiło w otwartą edycję zamówienia.
   */
  readonly activeChange = output<boolean>();

  private readonly orders = inject(OrdersService);
  private readonly injector = inject(Injector);

  protected readonly openForm = signal<OpenForm | null>(null);
  protected readonly pendingDelete = signal<PendingDelete | null>(null);
  protected readonly notice = signal<Notice | null>(null);
  protected readonly alert = signal<Alert | null>(null);
  protected readonly reloading = signal(false);
  protected readonly deletingId = signal<string | null>(null);
  /** Liczba trwających operacji wpisów poza formularzem (usuwanie, odświeżenie). */
  private readonly operations = signal(0);
  private readonly active = computed(() => this.openForm() !== null || this.operations() > 0);

  protected readonly cancelled = computed(() => this.order().fulfillmentStatus === 'cancelled');
  /** Akcje wpisów są niedostępne podczas edycji zamówienia, formularza wpisu i usuwania. */
  protected readonly actionsDisabled = computed(() => this.locked() || this.active());

  protected readonly stateLabels = PAYMENT_STATE_LABELS;
  protected readonly stateTones = PAYMENT_STATE_TONES;
  protected readonly paymentMethodLabels = PAYMENT_METHOD_LABELS;
  protected readonly refundMethodLabels = REFUND_METHOD_LABELS;
  protected readonly refundKindLabels = REFUND_KIND_LABELS;
  protected readonly formatDate = formatDate;
  protected readonly formatPln = formatPln;

  private readonly deleteDialog = viewChild<ConfirmDialog>('deleteDialog');
  private returnFocusTo: HTMLElement | null = null;

  constructor() {
    effect(() => {
      const active = this.active();
      untracked(() => this.activeChange.emit(active));
    });
  }

  protected readonly saveCorrection = (input: SalesCorrectionInput): Promise<EntryFormOutcome> =>
    this.save('salesCorrection', input);
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

  protected addCorrection(): void {
    this.open({
      type: 'salesCorrection',
      entry: null,
      initial: emptySalesCorrectionForm(),
      key: newKey(),
    });
  }

  protected editCorrection(entry: SalesCorrectionEntry): void {
    this.open({
      type: 'salesCorrection',
      entry,
      initial: salesCorrectionFormFrom(entry),
      key: null,
    });
  }

  protected async deleteCorrection(entry: SalesCorrectionEntry): Promise<void> {
    await this.delete({ type: 'salesCorrection', entry });
  }

  protected noticeIn(section: EntrySection): string | null {
    const notice = this.notice();
    return notice?.section === section ? notice.text : null;
  }

  protected alertIn(section: EntrySection): Alert | null {
    const alert = this.alert();
    return alert?.section === section ? alert : null;
  }

  protected editPayment(entry: PaymentEntry): void {
    this.open({ type: 'payment', entry, initial: paymentFormFrom(entry), key: null });
  }

  protected editRefund(entry: RefundEntry): void {
    this.open({ type: 'refund', entry, initial: refundFormFrom(entry), key: null });
  }

  protected closeForm(): void {
    const focus = this.detachForm();
    this.restoreFocus(focus.target, focus.section);
  }

  /** Zamyka formularz i zwraca cel fokusu (przycisk otwierający i sekcję wpisu). */
  private detachForm(): { target: HTMLElement | null; section: EntrySection } {
    const section = sectionOf(this.openForm()?.type ?? 'payment');
    this.openForm.set(null);
    this.alert.set(null);
    const target = this.returnFocusTo;
    this.returnFocusTo = null;
    return { target, section };
  }

  /**
   * Po renderowaniu fokus wraca na przycisk, który otworzył akcję; gdy go już nie ma (np. wiersz
   * usuniętego wpisu), trafia na nagłówek sekcji tego wpisu.
   */
  private restoreFocus(target: HTMLElement | null, section: EntrySection): void {
    afterNextRender(
      () => {
        const usable =
          target !== null &&
          target !== document.body &&
          target.isConnected &&
          !(target as HTMLButtonElement).disabled;
        if (usable) {
          target.focus();
        } else {
          document.getElementById(SECTION_HEADINGS[section])?.focus();
        }
      },
      { injector: this.injector },
    );
  }

  /** Po konflikcie: stan z serwera; otwarta poprawa dostaje aktualne dane wpisu. */
  protected async reloadCurrent(): Promise<void> {
    this.reloading.set(true);
    try {
      await this.track(async () => {
        const fresh = await this.reload()();
        if (!fresh) {
          return;
        }
        this.alert.set(null);
        const current = this.openForm();
        if (current?.entry) {
          this.refreshOpenEntry(current, fresh);
        }
      });
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
    switch (form.type) {
      case 'payment':
        return form.entry ? 'Popraw wpłatę' : 'Dodaj wpłatę';
      case 'refund':
        return form.entry ? 'Popraw zwrot' : 'Dodaj zwrot';
      case 'salesCorrection':
        return form.entry ? 'Popraw korektę sprzedaży' : 'Dodaj korektę sprzedaży';
    }
  }

  protected deleteHeading(pending: PendingDelete): string {
    switch (pending.type) {
      case 'payment':
        return 'Usunąć wpłatę?';
      case 'refund':
        return 'Usunąć zwrot?';
      case 'salesCorrection':
        return 'Usunąć korektę sprzedaży?';
    }
  }

  /** Skutek usunięcia w dialogu: wpisy pieniężne zmieniają Zapłacono, korekta obniżenia sprzedaży. */
  protected deleteEffect(pending: PendingDelete): string {
    return pending.type === 'salesCorrection'
      ? 'Zmieni się suma obniżeń sprzedaży. Ślad usunięcia zostanie w historii zmian.'
      : 'Zmieni się kwota Zapłacono i stan płatności. Ślad usunięcia zostanie w historii zmian.';
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
    if (pending.type === 'salesCorrection') {
      const { amount, date } = pending.entry;
      return `Korekta sprzedaży ${formatPln(amount)} z ${formatDate(date)} zostanie usunięta z zamówienia ${number}.`;
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
  }

  private refreshOpenEntry(current: OpenForm, order: OrderDetails): void {
    const id = current.entry?.id;
    if (current.type === 'payment') {
      const fresh = order.payments.find((entry) => entry.id === id);
      if (fresh) {
        this.openForm.set({ ...current, entry: fresh, initial: paymentFormFrom(fresh) });
        return;
      }
    } else if (current.type === 'refund') {
      const fresh = order.refunds.find((entry) => entry.id === id);
      if (fresh) {
        this.openForm.set({ ...current, entry: fresh, initial: refundFormFrom(fresh) });
        return;
      }
    } else {
      const fresh = order.salesCorrections.find((entry) => entry.id === id);
      if (fresh) {
        this.openForm.set({ ...current, entry: fresh, initial: salesCorrectionFormFrom(fresh) });
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
    const section = sectionOf(type);
    this.alert.set(null);
    const outcome: EntryOutcome = current.entry
      ? await this.orders.updateEntry(type, orderId, current.entry.id, input, current.entry.version)
      : await this.orders.addEntry(type, orderId, input, current.key ?? newKey());

    switch (outcome.kind) {
      case 'saved':
        await this.closeAndReload();
        this.notice.set({ section, text: NOTICES[type].saved });
        return null;
      case 'keyReused':
        await this.closeAndReload();
        this.alert.set({ section, text: ENTRY_KEY_REUSED_MESSAGE, reload: false });
        return null;
      case 'conflict':
        this.alert.set({ section, text: ENTRY_CONFLICT_MESSAGE, reload: true });
        return null;
      case 'entryNotFound':
        this.alert.set({ section, text: ENTRY_NOT_FOUND_MESSAGE, reload: true });
        return null;
      case 'orderNotFound':
        await this.closeAndReload();
        return null;
      case 'rejected':
        return { message: outcome.message };
      case 'error':
        return { error: outcome.error };
    }
  }

  /**
   * Formularz zamyka się już w trakcie operacji, więc „Edytuj” pozostaje zablokowane bez
   * przerwy do końca odświeżenia.
   */
  private async closeAndReload(): Promise<void> {
    let focus: { target: HTMLElement | null; section: EntrySection } | null = null;
    await this.track(async () => {
      focus = this.detachForm();
      await this.reload()();
    });
    // Fokus dopiero po odświeżeniu: wtedy wiadomo, czy przycisk otwierający jeszcze istnieje.
    if (focus) {
      const { target, section } = focus;
      this.restoreFocus(target, section);
    }
  }

  private async track(operation: () => Promise<void>): Promise<void> {
    this.operations.update((count) => count + 1);
    try {
      await operation();
    } finally {
      this.operations.update((count) => count - 1);
    }
  }

  private async delete(pending: PendingDelete): Promise<void> {
    if (this.actionsDisabled()) {
      return;
    }
    // Od otwarcia dialogu do końca odświeżenia trwa operacja wpisu.
    // Przycisk „Usuń” w wierszu: po usunięciu wiersza fokus trafi na nagłówek sekcji.
    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    await this.track(() => this.confirmAndDelete(pending, opener));
  }

  private async confirmAndDelete(
    pending: PendingDelete,
    opener: HTMLElement | null,
  ): Promise<void> {
    this.pendingDelete.set(pending);
    if (!(await this.deleteDialog()?.open())) {
      return;
    }
    const { type, entry } = pending;
    const section = sectionOf(type);
    this.notice.set(null);
    this.alert.set(null);
    this.deletingId.set(entry.id);
    try {
      const outcome = await this.orders.deleteEntry(type, this.order().id, entry.id, entry.version);
      switch (outcome.kind) {
        case 'saved':
          await this.reload()();
          this.notice.set({ section, text: NOTICES[type].deleted });
          this.restoreFocus(opener, section);
          return;
        case 'conflict':
          this.alert.set({ section, text: ENTRY_CONFLICT_MESSAGE, reload: true });
          return;
        case 'entryNotFound':
          this.alert.set({ section, text: ENTRY_NOT_FOUND_MESSAGE, reload: true });
          return;
        case 'orderNotFound':
          await this.reload()();
          return;
        case 'rejected':
          this.alert.set({ section, text: outcome.message, reload: false });
          return;
        case 'error':
          this.alert.set({
            section,
            text: entryErrorMessage(outcome.error, DELETE_FAILED_MESSAGE),
            reload: false,
          });
          return;
        case 'keyReused':
          // Usuwanie nie używa klucza idempotencji; odpowiedź traktujemy jak nieudaną.
          this.alert.set({ section, text: DELETE_FAILED_MESSAGE, reload: true });
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
