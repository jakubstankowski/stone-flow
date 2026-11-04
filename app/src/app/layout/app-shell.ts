import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  ActivatedRouteSnapshot,
  NavigationEnd,
  Router,
  RouterLink,
  RouterOutlet,
} from '@angular/router';
import { filter, map } from 'rxjs';
import { MainNav } from './main-nav';
import { PRODUCT_NAME } from './nav-items';

/** Powłoka aplikacji: pominięcie nawigacji, nagłówek, nawigacja i obszar treści. */
@Component({
  selector: 'app-shell',
  imports: [MainNav, RouterLink, RouterOutlet],
  templateUrl: './app-shell.html',
  host: { class: 'flex min-h-screen flex-col' },
})
export class AppShell {
  protected readonly productName = PRODUCT_NAME;

  private readonly router = inject(Router);

  protected readonly section = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => sectionOf(this.router.routerState.snapshot.root)),
    ),
    { initialValue: sectionOf(this.router.routerState.snapshot.root) },
  );

  protected skipToContent(event: Event): void {
    event.preventDefault();
    document.getElementById('tresc')?.focus();
  }
}

function sectionOf(root: ActivatedRouteSnapshot): string | undefined {
  let route: ActivatedRouteSnapshot | null = root;
  let section: string | undefined;
  while (route) {
    const value: unknown = route.data['section'];
    if (typeof value === 'string') {
      section = value;
    }
    route = route.firstChild;
  }
  return section;
}
