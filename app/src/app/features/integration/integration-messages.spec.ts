import { NETWORK_ERROR_MESSAGE } from '../../core/http/api-error';
import { orderError } from '../../../testing/integration-testing';
import {
  CSRF_MESSAGE,
  RETRY_GENERIC_MESSAGE,
  retryMessage,
  syncMessage,
} from './integration-messages';

const csrf = { kind: 'problem', status: 400, detail: null, code: 'csrf_invalid' } as const;

describe('syncMessage', () => {
  it('informs about an accepted or running sync and refreshes the status', () => {
    expect(syncMessage({ kind: 'accepted' })).toEqual({
      text: 'Synchronizacja uruchomiona. Stan odświeży się automatycznie.',
      alert: false,
      refresh: true,
      reload: false,
    });
    expect(syncMessage({ kind: 'inProgress', detail: null })).toMatchObject({
      text: 'Synchronizacja już trwa. Poczekaj na jej zakończenie.',
      alert: false,
      refresh: true,
    });
  });

  it('prefers the API detail', () => {
    expect(syncMessage({ kind: 'notConfigured', detail: 'Brak kluczy.' }).text).toBe(
      'Brak kluczy.',
    );
  });

  it('refreshes after a time-out to learn whether the sync started', () => {
    expect(syncMessage({ kind: 'timeout' })).toMatchObject({ alert: true, refresh: true });
  });

  it('does not refresh after CSRF and network errors', () => {
    expect(syncMessage({ kind: 'error', error: csrf })).toEqual({
      text: CSRF_MESSAGE,
      alert: true,
      refresh: false,
      reload: false,
    });
    expect(syncMessage({ kind: 'error', error: { kind: 'network' } })).toEqual({
      text: NETWORK_ERROR_MESSAGE,
      alert: true,
      refresh: false,
      reload: false,
    });
  });
});

describe('retryMessage', () => {
  it('confirms a closed error', () => {
    expect(retryMessage({ kind: 'done', error: orderError({ status: 'closed' }) }, 'Z/1')).toEqual({
      text: 'Zamówienie Z/1 zapisane. Błąd zamknięty.',
      alert: false,
      refresh: true,
      reload: true,
    });
  });

  it('says when the error stays open', () => {
    expect(retryMessage({ kind: 'done', error: orderError() }, 'Z/1').text).toBe(
      'Zamówienie Z/1 zapisane, ale błąd pozostaje otwarty. Spróbuj ponownie później.',
    );
  });

  it.each([
    ['notFound', 'Nie znaleziono błędu synchronizacji.'],
    ['closed', 'Ten błąd jest już zamknięty.'],
    [
      'shopFailed',
      'Sklep nie zwrócił zamówienia. Błąd pozostaje otwarty; spróbuj ponownie później.',
    ],
    [
      'unavailable',
      'Nie udało się zapisać zamówienia. Błąd pozostaje otwarty; spróbuj ponownie później.',
    ],
  ] as const)('reloads after %s with a fallback text', (kind, text) => {
    expect(retryMessage({ kind, detail: null }, 'Z/1')).toEqual({
      text,
      alert: true,
      refresh: true,
      reload: true,
    });
    expect(retryMessage({ kind, detail: 'Z API' }, 'Z/1').text).toBe('Z API');
  });

  it('reloads after a time-out because the server result is unknown', () => {
    expect(retryMessage({ kind: 'timeout' }, 'Z/1')).toEqual({
      text: RETRY_GENERIC_MESSAGE,
      alert: true,
      refresh: true,
      reload: true,
    });
  });

  it('shows a generic message and reloads after 404 without a code', () => {
    const error = { kind: 'problem', status: 404, detail: null, code: null } as const;
    expect(retryMessage({ kind: 'error', error }, 'Z/1')).toMatchObject({
      text: RETRY_GENERIC_MESSAGE,
      reload: true,
      refresh: true,
    });
  });

  it('neither reloads nor refreshes after CSRF and network errors', () => {
    expect(retryMessage({ kind: 'error', error: csrf }, 'Z/1')).toMatchObject({
      text: CSRF_MESSAGE,
      refresh: false,
      reload: false,
    });
    expect(retryMessage({ kind: 'error', error: { kind: 'network' } }, 'Z/1')).toMatchObject({
      text: NETWORK_ERROR_MESSAGE,
      refresh: false,
      reload: false,
    });
  });
});
