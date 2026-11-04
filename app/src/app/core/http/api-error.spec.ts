import { HttpErrorResponse } from '@angular/common/http';
import { fieldErrorOf, toApiError } from './api-error';

function response(status: number, error: unknown = null): HttpErrorResponse {
  return new HttpErrorResponse({ status, error });
}

describe('toApiError', () => {
  it.each([0, 500, 502, 503])('maps status %i to a network error', (status) => {
    expect(toApiError(response(status))).toEqual({ kind: 'network' });
  });

  it('maps a non-HTTP failure to a network error', () => {
    expect(toApiError(new Error('boom'))).toEqual({ kind: 'network' });
  });

  it('maps 400 with errors to field errors, comparing names without case', () => {
    const error = toApiError(
      response(400, {
        title: 'One or more validation errors occurred.',
        errors: {
          NewPassword: ['Hasło musi mieć co najmniej 12 znaków.'],
          email: ['Podaj adres e-mail.'],
        },
      }),
    );

    expect(error.kind).toBe('validation');
    expect(fieldErrorOf(error, 'newPassword')).toBe('Hasło musi mieć co najmniej 12 znaków.');
    expect(fieldErrorOf(error, 'EMAIL')).toBe('Podaj adres e-mail.');
    expect(fieldErrorOf(error, 'password')).toBeNull();
  });

  it('maps other errors to a problem with detail and code', () => {
    expect(
      toApiError(
        response(400, {
          detail: 'Link wygasł lub został już użyty. Poproś o nowy link.',
          code: 'reset_link_invalid',
          traceId: '00-abc',
        }),
      ),
    ).toEqual({
      kind: 'problem',
      status: 400,
      detail: 'Link wygasł lub został już użyty. Poproś o nowy link.',
      code: 'reset_link_invalid',
    });
  });

  it('tolerates a problem without a body', () => {
    expect(toApiError(response(401))).toEqual({
      kind: 'problem',
      status: 401,
      detail: null,
      code: null,
    });
    expect(fieldErrorOf(toApiError(response(401)), 'email')).toBeNull();
  });
});
