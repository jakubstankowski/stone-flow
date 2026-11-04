import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { PRODUCT_NAME } from './nav-items';

/** Prosty układ ekranów logowania i resetu hasła: nazwa produktu i karta, bez nawigacji. */
@Component({
  selector: 'app-auth-layout',
  imports: [RouterOutlet],
  templateUrl: './auth-layout.html',
  host: { class: 'flex min-h-screen flex-col items-center px-4 py-10' },
})
export class AuthLayout {
  protected readonly productName = PRODUCT_NAME;
}
