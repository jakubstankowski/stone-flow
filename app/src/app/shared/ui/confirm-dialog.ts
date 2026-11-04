import { Component, ElementRef, input, viewChild } from '@angular/core';
import { Button } from './button';

let nextId = 0;

/**
 * Dialog potwierdzenia na natywnym `<dialog>` (design guide, sekcje 6 i 9).
 * `open()` zwraca `true` po akcji i `false` po „Anuluj” lub Escape; fokus wraca do
 * elementu aktywnego przed otwarciem.
 */
@Component({
  selector: 'app-confirm-dialog',
  imports: [Button],
  templateUrl: './confirm-dialog.html',
})
export class ConfirmDialog {
  readonly heading = input.required<string>();
  readonly confirmLabel = input.required<string>();
  readonly tone = input<'primary' | 'danger'>('danger');

  protected readonly titleId = `potwierdzenie-${nextId++}`;

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly actions = viewChild.required<ElementRef<HTMLElement>>('actions');

  private returnFocusTo: HTMLElement | null = null;
  private resolveResult: ((confirmed: boolean) => void) | null = null;

  open(): Promise<boolean> {
    this.resolveResult?.(false);
    const active = document.activeElement;
    this.returnFocusTo = active instanceof HTMLElement ? active : null;

    this.dialog().nativeElement.showModal();
    this.actions().nativeElement.querySelector('button')?.focus();

    return new Promise<boolean>((resolve) => {
      this.resolveResult = resolve;
    });
  }

  protected onCancel(event: Event): void {
    event.preventDefault();
    this.finish(false);
  }

  /** Zamknięcie z zewnątrz (np. przez przeglądarkę) kończy oczekiwanie jak „Anuluj”. */
  protected onClose(): void {
    if (this.resolveResult) {
      this.finish(false);
    }
  }

  protected finish(confirmed: boolean): void {
    // Najpierw odpinamy oczekujący wynik: `close()` wywołuje zdarzenie `close`,
    // które nie może już rozstrzygnąć go jako „Anuluj”.
    const resolve = this.resolveResult;
    this.resolveResult = null;

    const dialog = this.dialog().nativeElement;
    if (dialog.open) {
      dialog.close();
    }
    this.restoreFocus();
    resolve?.(confirmed);
  }

  private restoreFocus(): void {
    const target = this.returnFocusTo;
    this.returnFocusTo = null;
    if (target?.isConnected) {
      target.focus();
      return;
    }
    document.getElementById('tresc')?.focus();
  }
}
