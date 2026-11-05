import { Routes } from '@angular/router';

/** Trasy sekcji „Zamówienia” (wewnątrz powłoki, leniwie ładowane). */
export const ORDERS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./orders-list-page').then((m) => m.OrdersListPage),
  },
  {
    path: 'nowe',
    title: 'Dodaj zamówienie',
    loadComponent: () => import('./order-create-page').then((m) => m.OrderCreatePage),
  },
  {
    path: ':id',
    title: 'Zamówienie',
    loadComponent: () => import('./order-details-page').then((m) => m.OrderDetailsPage),
  },
];
