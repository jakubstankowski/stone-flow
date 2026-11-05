import { Component, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';
import { NO_DATA, formatDate, formatDateTime, formatPln } from '../../shared/format/pl-format';
import { Button } from '../../shared/ui/button';
import { ConfirmDialog } from '../../shared/ui/confirm-dialog';
import { EmptyState } from '../../shared/ui/empty-state';
import { ErrorState } from '../../shared/ui/error-state';
import { LoadingState } from '../../shared/ui/loading-state';
import { StatusBadge } from '../../shared/ui/status-badge';
import { OrderForm, OrderFormOutcome } from './order-form';
import { OrderPayments } from './order-payments';
import { OrderFormModel, orderErrorMessage, orderFormFrom } from './order-form-model';
import {
  CHANNEL_LABELS,
  CUSTOMER_KIND_LABELS,
  OrderDetails,
  OrderInput,
  SOURCE_LABELS,
  STATUS_LABELS,
  STATUS_TONES,
} from './order-models';
import { ChangeOrderOutcome, OrdersService } from './orders.service';

export const CONFLICT_MESSAGE = 'Zamówienie zostało zmienione w międzyczasie.';
const STATE_CHANGED_MESSAGE = 'Stan zamówienia zmienił się w międzyczasie.';

type ViewStatus = 'loading' | 'notFound' | 'error' | 'loaded';

interface Alert {
  readonly text: string;
  /** Pokazuje akcję „Wczytaj aktualne dane”. */
  readonly reload: boolean;
}

/**
 * Ekran `/zamowienia/:id`: szczegóły, edycja z `version`, konflikt `409 order_conflict`
 * z „Wczytaj aktualne dane” oraz anulowanie i przywracanie po potwierdzeniu. Zamówień się nie
 * usuwa; usuwać można tylko wpisy płatności w sekcji „Płatności” (SF-9). Edycja zamówienia
 * i formularz wpisu wykluczają się, żeby odświeżenie po zapisie wpisu nie skasowało
 * niezapisanych zmian zamówienia.
 */
@Component({
  selector: 'app-order-details-page',
  imports: [
    Button,
    ConfirmDialog,
    EmptyState,
    ErrorState,
    LoadingState,
    OrderForm,
    OrderPayments,
    StatusBadge,
  ],
  templateUrl: './order-details-page.html',
})
export class OrderDetailsPage {
  private readonly orders = inject(OrdersService);
  private readonly router = inject(Router);

  private readonly id = toSignal(
    inject(ActivatedRoute).paramMap.pipe(map((params) => params.get('id') ?? '')),
    { requireSync: true },
  );

  protected readonly status = signal<ViewStatus>('loading');
  protected readonly order = signal<OrderDetails | null>(null);
  protected readonly editing = signal(false);
  protected readonly formInitial = signal<OrderFormModel | null>(null);
  protected readonly notice = signal<string | null>(this.orders.takeNotice());
  protected readonly alert = signal<Alert | null>(null);
  protected readonly reloading = signal(false);
  protected readonly actionBusy = signal<'cancel' | 'restore' | null>(null);
  /** W sekcji „Płatności” jest otwarty formularz wpisu (wtedy „Edytuj” jest wyłączone). */
  protected readonly entryFormOpen = signal(false);

  protected readonly cancelled = computed(() => this.order()?.fulfillmentStatus === 'cancelled');
  protected readonly statusLabels = STATUS_LABELS;
  protected readonly statusTones = STATUS_TONES;
  protected readonly sourceLabels = SOURCE_LABELS;
  protected readonly channelLabels = CHANNEL_LABELS;
  protected readonly customerKindLabels = CUSTOMER_KIND_LABELS;
  protected readonly noData = NO_DATA;
  protected readonly formatDate = formatDate;
  protected readonly formatDateTime = formatDateTime;
  protected readonly formatPln = formatPln;

  private readonly cancelDialog = viewChild<ConfirmDialog>('cancelDialog');
  private readonly restoreDialog = viewChild<ConfirmDialog>('restoreDialog');

  protected readonly save = (input: OrderInput): Promise<OrderFormOutcome> => this.update(input);
  /** Odświeżenie po zmianie wpisu płatności; zwraca wczytane zamówienie albo `null`. */
  protected readonly reloadOrder = async (): Promise<OrderDetails | null> => {
    await this.load(this.id(), { keepView: true });
    return this.status() === 'loaded' && !this.alert() ? this.order() : null;
  };

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => void this.load(id));
    });
  }

  protected retry(): Promise<void> {
    return this.load(this.id());
  }

  /** Zastępuje widok i formularz stanem z serwera (po konflikcie albo zmianie stanu). */
  protected async reloadCurrent(): Promise<void> {
    this.reloading.set(true);
    try {
      await this.load(this.id(), { keepView: true });
    } finally {
      this.reloading.set(false);
    }
  }

  protected startEdit(): void {
    const order = this.order();
    if (!order || this.entryFormOpen()) {
      return;
    }
    this.notice.set(null);
    this.alert.set(null);
    this.formInitial.set(orderFormFrom(order));
    this.editing.set(true);
  }

  protected stopEdit(): void {
    this.editing.set(false);
    this.alert.set(null);
  }

  protected onEntryFormOpen(open: boolean): void {
    this.entryFormOpen.set(open);
    if (open) {
      this.notice.set(null);
      this.alert.set(null);
    }
  }

  protected goToList(): void {
    void this.router.navigate(['/zamowienia']);
  }

  protected async cancelOrder(): Promise<void> {
    const order = this.order();
    if (!order || this.actionBusy() || !(await this.cancelDialog()?.open())) {
      return;
    }
    await this.runAction('cancel', () => this.orders.cancel(order.id, order.version));
  }

  protected async restoreOrder(): Promise<void> {
    const order = this.order();
    if (!order || this.actionBusy() || !(await this.restoreDialog()?.open())) {
      return;
    }
    await this.runAction('restore', () => this.orders.restore(order.id, order.version));
  }

  protected list(items: readonly string[]): string {
    return items.length > 0 ? items.join(', ') : NO_DATA;
  }

  protected address(order: OrderDetails): string {
    const { street, postalCode, city } = order.customer;
    const place = [postalCode, city].filter(Boolean).join(' ');
    const parts = [street, place].filter(Boolean);
    return parts.length > 0 ? parts.join(', ') : NO_DATA;
  }

  private async update(input: OrderInput): Promise<OrderFormOutcome> {
    const order = this.order();
    if (!order) {
      return null;
    }
    this.alert.set(null);
    const outcome = await this.orders.update(order.id, input, order.version);
    if (outcome.kind === 'error') {
      return { error: outcome.error };
    }
    await this.handleChange(outcome);
    return null;
  }

  private async runAction(
    action: 'cancel' | 'restore',
    call: () => Promise<ChangeOrderOutcome>,
  ): Promise<void> {
    this.actionBusy.set(action);
    this.notice.set(null);
    this.alert.set(null);
    try {
      const outcome = await call();
      if (outcome.kind === 'error') {
        this.alert.set({ text: orderErrorMessage(outcome.error), reload: false });
        return;
      }
      await this.handleChange(outcome);
    } finally {
      this.actionBusy.set(null);
    }
  }

  private async handleChange(
    outcome: Exclude<ChangeOrderOutcome, { kind: 'error' }>,
  ): Promise<void> {
    switch (outcome.kind) {
      case 'saved': {
        const notice = this.orders.takeNotice();
        this.editing.set(false);
        await this.load(this.id(), { keepView: true });
        this.notice.set(notice);
        return;
      }
      case 'conflict':
        this.alert.set({ text: CONFLICT_MESSAGE, reload: true });
        return;
      case 'stateChanged':
        this.alert.set({ text: outcome.detail ?? STATE_CHANGED_MESSAGE, reload: true });
        return;
      case 'notFound':
        this.editing.set(false);
        this.status.set('notFound');
        return;
    }
  }

  private async load(id: string, options: { keepView?: boolean } = {}): Promise<void> {
    if (!options.keepView) {
      this.status.set('loading');
    }
    const outcome = await this.orders.loadOrder(id);
    if (id !== this.id()) {
      return;
    }
    switch (outcome.kind) {
      case 'loaded':
        this.order.set(outcome.order);
        this.formInitial.set(orderFormFrom(outcome.order));
        this.alert.set(null);
        this.status.set('loaded');
        return;
      case 'notFound':
        this.editing.set(false);
        this.status.set('notFound');
        return;
      case 'error':
        if (options.keepView && this.order()) {
          this.alert.set({ text: orderErrorMessage(outcome.error), reload: true });
        } else {
          this.status.set('error');
        }
        return;
    }
  }
}
