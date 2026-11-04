import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import {
  FormField,
  TreeValidationResult,
  form,
  minLength,
  required,
  submit,
  validate,
} from '@angular/forms/signals';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthSession, ResetLink } from '../../core/auth/auth-session';
import { Button } from '../../shared/ui/button';
import { Field } from '../../shared/ui/field';
import {
  NEW_PASSWORD_MIN_LENGTH,
  RESET_LINK_INVALID_MESSAGE,
  VALIDATION_MESSAGES,
  formErrorMessage,
  serverFieldErrors,
  visibleError,
} from './auth-messages';
import { parseResetLink } from './reset-link';

interface SetPasswordModel {
  newPassword: string;
  repeatPassword: string;
}

/**
 * Ekran `/reset-hasla/nowe` (spec, punkty 16, 18–20). Fragment `#konto=…&token=…` jest
 * czytany raz i od razu usuwany z historii, przed jakimkolwiek żądaniem. Token zostaje
 * tylko w prywatnym polu: nie trafia do sygnału, szablonu, URL ani konsoli.
 */
@Component({
  selector: 'app-set-password-page',
  imports: [Button, Field, FormField, RouterLink],
  templateUrl: './set-password-page.html',
})
export class SetPasswordPage {
  private readonly session = inject(AuthSession);
  private readonly injector = inject(Injector);
  private readonly link: ResetLink | null;

  protected readonly linkInvalidMessage = RESET_LINK_INVALID_MESSAGE;
  protected readonly linkInvalid = signal(false);
  protected readonly formError = signal<string | null>(null);

  private readonly model = signal<SetPasswordModel>({ newPassword: '', repeatPassword: '' });
  protected readonly form = form(this.model, (path) => {
    required(path.newPassword, { message: VALIDATION_MESSAGES.newPasswordRequired });
    minLength(path.newPassword, NEW_PASSWORD_MIN_LENGTH, {
      message: VALIDATION_MESSAGES.newPasswordTooShort,
    });
    validate(path.repeatPassword, ({ value, valueOf }) =>
      value() === valueOf(path.newPassword)
        ? null
        : { kind: 'mismatch', message: VALIDATION_MESSAGES.passwordsDiffer },
    );
  });
  protected readonly busy = computed(() => this.form().submitting());
  protected readonly newPasswordError = computed(() => visibleError(this.form.newPassword));
  protected readonly repeatPasswordError = computed(() => visibleError(this.form.repeatPassword));

  private readonly resultMessage = viewChild<ElementRef<HTMLElement>>('resultMessage');

  constructor() {
    const route = inject(ActivatedRoute);
    this.link = parseResetLink(route.snapshot.fragment);
    this.linkInvalid.set(this.link === null);
    if (route.snapshot.fragment !== null) {
      void inject(Router).navigate([], { relativeTo: route, replaceUrl: true });
    }
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    this.formError.set(null);
    await submit(this.form, () => this.confirm());
    this.focusResult();
  }

  private async confirm(): Promise<TreeValidationResult> {
    if (!this.link) {
      this.linkInvalid.set(true);
      return null;
    }
    const error = await this.session.confirmPasswordReset(this.link, this.model().newPassword);
    if (!error) {
      await this.session.passwordSet();
      return null;
    }
    if (error.kind === 'problem' && error.code === 'reset_link_invalid') {
      this.linkInvalid.set(true);
      return null;
    }
    const fieldErrors = serverFieldErrors(error, { newPassword: this.form.newPassword });
    if (fieldErrors.length > 0) {
      return fieldErrors;
    }
    this.formError.set(formErrorMessage(error));
    return null;
  }

  /** Fokus po wyniku: komunikat (nieważny link, błąd ogólny) albo pierwsze pole z błędem. */
  private focusResult(): void {
    if (this.linkInvalid() || this.formError()) {
      afterNextRender(() => this.resultMessage()?.nativeElement.focus(), {
        injector: this.injector,
      });
      return;
    }
    const firstInvalid = [this.form.newPassword, this.form.repeatPassword].find((field) =>
      field().invalid(),
    );
    firstInvalid?.().focusBoundControl();
  }
}
