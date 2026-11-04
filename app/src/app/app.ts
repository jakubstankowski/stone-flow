import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthSession } from './core/auth/auth-session';
import { LoadingState } from './shared/ui/loading-state';

/**
 * Korzeń aplikacji: układ wybiera trasa (powłoka albo ekrany logowania). Do czasu odpowiedzi
 * `GET /api/auth/session` chronione trasy nie pokazują treści, tylko stan ładowania.
 */
@Component({
  selector: 'app-root',
  imports: [LoadingState, RouterOutlet],
  templateUrl: './app.html',
})
export class App {
  protected readonly checking = inject(AuthSession).checking;
}
