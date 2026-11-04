import { Component, inject, signal } from '@angular/core';
import { AuthSession } from '../core/auth/auth-session';
import { formErrorMessage } from '../features/auth/auth-messages';
import { Button } from '../shared/ui/button';

/** Menu użytkownika w nagłówku powłoki: adres e-mail i „Wyloguj” (spec, punkty 13 i 29). */
@Component({
  selector: 'app-user-menu',
  imports: [Button],
  templateUrl: './user-menu.html',
  host: {
    class:
      'flex basis-full min-w-0 flex-wrap items-center gap-x-3 gap-y-1 lg:ml-auto lg:basis-auto',
  },
})
export class UserMenu {
  private readonly session = inject(AuthSession);

  protected readonly email = this.session.email;
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async logout(): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    const error = await this.session.logout();
    this.busy.set(false);
    // `401` oznacza, że sesja już wygasła: interceptor przeniósł na ekran logowania.
    if (error && !(error.kind === 'problem' && error.status === 401)) {
      this.error.set(formErrorMessage(error));
    }
  }
}
