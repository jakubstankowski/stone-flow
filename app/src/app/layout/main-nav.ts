import { Component, DestroyRef, ElementRef, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { filter } from 'rxjs';
import { NAV_ITEMS } from './nav-items';

/**
 * Nawigacja główna: pozioma od 1024 px, poniżej przycisk „Menu” rozwijający listę
 * (design guide, sekcje 3 i 9).
 */
@Component({
  selector: 'app-main-nav',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './main-nav.html',
  host: { class: 'contents', '(keydown.escape)': 'closeAndFocusButton()' },
})
export class MainNav {
  protected readonly items = NAV_ITEMS;
  protected readonly open = signal(false);

  private readonly menuButton = viewChild.required<ElementRef<HTMLButtonElement>>('menuButton');

  constructor() {
    inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe(() => this.open.set(false));
  }

  protected toggle(): void {
    this.open.update((open) => !open);
  }

  protected closeAndFocusButton(): void {
    if (!this.open()) {
      return;
    }
    this.open.set(false);
    this.menuButton().nativeElement.focus();
  }
}
