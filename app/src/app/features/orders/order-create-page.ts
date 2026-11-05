import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Button } from '../../shared/ui/button';
import { OrderForm, OrderFormOutcome } from './order-form';
import { OrderFormModel, emptyOrderForm } from './order-form-model';
import { OrderInput } from './order-models';
import { OrdersService } from './orders.service';

interface KeyReused {
  readonly orderId: string;
  readonly orderNumber: string;
}

/**
 * Ekran `/zamowienia/nowe`. Klucz idempotencji powstaje przy otwarciu i zostaje do udanego
 * zapisu, więc ponowienie po utracie odpowiedzi nie tworzy duplikatu. Po
 * `409 idempotency_key_reused` nowy klucz daje tylko „Dodaj kolejne zamówienie”.
 */
@Component({
  selector: 'app-order-create-page',
  imports: [Button, OrderForm, RouterLink],
  templateUrl: './order-create-page.html',
})
export class OrderCreatePage {
  private readonly orders = inject(OrdersService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);

  private idempotencyKey = crypto.randomUUID();

  protected readonly initial = signal<OrderFormModel>(emptyOrderForm());
  protected readonly keyReused = signal<KeyReused | null>(null);

  private readonly reusedMessage = viewChild<ElementRef<HTMLElement>>('reusedMessage');

  protected readonly save = (input: OrderInput): Promise<OrderFormOutcome> => this.create(input);

  protected startNext(): void {
    this.idempotencyKey = crypto.randomUUID();
    this.keyReused.set(null);
    this.initial.set(emptyOrderForm());
  }

  protected goToList(): void {
    void this.router.navigate(['/zamowienia']);
  }

  private async create(input: OrderInput): Promise<OrderFormOutcome> {
    this.keyReused.set(null);
    const outcome = await this.orders.create(input, this.idempotencyKey);
    switch (outcome.kind) {
      case 'saved':
        await this.router.navigate(['/zamowienia', outcome.id]);
        return null;
      case 'keyReused':
        this.keyReused.set({ orderId: outcome.orderId, orderNumber: outcome.orderNumber });
        afterNextRender(() => this.reusedMessage()?.nativeElement.focus(), {
          injector: this.injector,
        });
        return null;
      case 'error':
        return { error: outcome.error };
    }
  }
}
