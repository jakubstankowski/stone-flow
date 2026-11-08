import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/**
 * Podsekcja „Wydatki” nad listami: „Wszystkie wydatki” i „Wydatki cykliczne”. Główna nawigacja
 * się nie zmienia (design guide §3); bieżący link ma `aria-current="page"`.
 */
@Component({
  selector: 'app-expenses-subnav',
  imports: [RouterLink, RouterLinkActive],
  template: `<nav aria-label="Podsekcje wydatków">
    <ul class="flex flex-wrap gap-x-6 gap-y-1 border-b border-(--sf-secondary)">
      <li>
        <a
          routerLink="/wydatki"
          routerLinkActive="border-(--sf-accent-text) font-semibold"
          ariaCurrentWhenActive="page"
          [routerLinkActiveOptions]="exact"
          class="-mb-px inline-flex min-h-11 items-center border-b-2 border-transparent px-1"
          >Wszystkie wydatki</a
        >
      </li>
      <li>
        <a
          routerLink="/wydatki/cykliczne"
          routerLinkActive="border-(--sf-accent-text) font-semibold"
          ariaCurrentWhenActive="page"
          [routerLinkActiveOptions]="exact"
          class="-mb-px inline-flex min-h-11 items-center border-b-2 border-transparent px-1"
          >Wydatki cykliczne</a
        >
      </li>
    </ul>
  </nav>`,
})
export class ExpensesSubnav {
  protected readonly exact = {
    paths: 'exact',
    queryParams: 'ignored',
    fragment: 'ignored',
    matrixParams: 'ignored',
  } as const;
}
