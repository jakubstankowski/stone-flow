import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { PRODUCT_NAME } from './nav-items';

/** Tytuł dokumentu „{Sekcja} · Stone Flow”. */
@Injectable({ providedIn: 'root' })
export class PageTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const section = this.buildTitle(snapshot);
    this.title.setTitle(section ? `${section} · ${PRODUCT_NAME}` : PRODUCT_NAME);
  }
}
