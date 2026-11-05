import { FieldTree, ValidationError } from '@angular/forms/signals';
import { EntryFormOutcome, entryErrorMessage, ENTRY_MESSAGES } from './entry-form-model';

/** Pole formularza wpisu: klucz błędu API (ścieżka JSON), id kontrolki i drzewo pola. */
export interface EntryField {
  readonly key: string;
  readonly id: string;
  readonly field: () => FieldTree<string>;
}

export interface SummaryItem {
  readonly id: string | null;
  readonly message: string;
}

/** Błąd pola do wyświetlenia: po dotknięciu pola albo próbie zapisu. */
export function shownError<T>(field: FieldTree<T>): string | null {
  const state = field();
  if (!state.touched()) {
    return null;
  }
  return state.errors().find((error) => error.message)?.message ?? null;
}

/** Pozycje podsumowania błędów: najpierw pola w kolejności formularza, potem błędy ogólne. */
export function summaryOf(
  fields: readonly EntryField[],
  general: readonly string[],
): readonly SummaryItem[] {
  const items: SummaryItem[] = [];
  for (const entry of fields) {
    const message = shownError(entry.field());
    if (message) {
      items.push({ id: entry.id, message });
    }
  }
  for (const message of general) {
    items.push({ id: null, message });
  }
  return items;
}

/**
 * Wynik zapisu → błędy pól (z kluczy `ValidationProblemDetails`) i komunikaty ogólne
 * (np. `version`, reguła `422`, sieć).
 */
export function outcomeErrors(
  outcome: EntryFormOutcome,
  fields: readonly EntryField[],
): { fieldErrors: ValidationError.WithFieldTree[]; general: string[] } {
  if (!outcome) {
    return { fieldErrors: [], general: [] };
  }
  if ('message' in outcome) {
    return { fieldErrors: [], general: [outcome.message] };
  }
  const { error } = outcome;
  if (error.kind !== 'validation') {
    return { fieldErrors: [], general: [entryErrorMessage(error)] };
  }
  const fieldErrors: ValidationError.WithFieldTree[] = [];
  const general: string[] = [];
  for (const [key, messages] of Object.entries(error.fieldErrors)) {
    const normalized = key.replace(/^\$\./, '');
    const entry = fields.find((candidate) => candidate.key.toLowerCase() === normalized);
    for (const message of messages) {
      if (entry) {
        fieldErrors.push({ kind: 'server', message, fieldTree: entry.field() });
      } else {
        general.push(message);
      }
    }
  }
  if (fieldErrors.length === 0 && general.length === 0) {
    general.push(error.detail ?? ENTRY_MESSAGES.saveFailed);
  }
  return { fieldErrors, general };
}

export function focusById(event: Event, id: string): void {
  event.preventDefault();
  document.getElementById(id)?.focus();
}
