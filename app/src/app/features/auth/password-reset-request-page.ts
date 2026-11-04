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
  email,
  form,
  required,
  submit,
} from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { AuthSession } from '../../core/auth/auth-session';
import { Button } from '../../shared/ui/button';
import { Field } from '../../shared/ui/field';
import {
  RESET_SENT_MESSAGE,
  VALIDATION_MESSAGES,
  formErrorMessage,
  serverFieldErrors,
  visibleError,
} from './auth-messages';

interface ResetRequestModel {
  email: string;
}

/** Ekran `/reset-hasla`: prośba o link (spec, punkty 15 i 17). */
@Component({
  selector: 'app-password-reset-request-page',
  imports: [Button, Field, FormField, RouterLink],
  templateUrl: './password-reset-request-page.html',
})
export class PasswordResetRequestPage {
  private readonly session = inject(AuthSession);
  private readonly injector = inject(Injector);

  protected readonly sentMessage = RESET_SENT_MESSAGE;
  protected readonly sent = signal(false);
  protected readonly formError = signal<string | null>(null);

  private readonly model = signal<ResetRequestModel>({ email: '' });
  protected readonly form = form(this.model, (path) => {
    required(path.email, { message: VALIDATION_MESSAGES.emailRequired });
    email(path.email, { message: VALIDATION_MESSAGES.emailFormat });
  });
  protected readonly busy = computed(() => this.form().submitting());
  protected readonly emailError = computed(() => visibleError(this.form.email));

  private readonly resultMessage = viewChild<ElementRef<HTMLElement>>('resultMessage');

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    this.formError.set(null);
    this.sent.set(false);
    await submit(this.form, () => this.request());
    this.focusResult();
  }

  private async request(): Promise<TreeValidationResult> {
    const error = await this.session.requestPasswordReset(this.model().email.trim());
    if (!error) {
      this.sent.set(true);
      return null;
    }
    const fieldErrors = serverFieldErrors(error, { email: this.form.email });
    if (fieldErrors.length > 0) {
      return fieldErrors;
    }
    this.formError.set(formErrorMessage(error));
    return null;
  }

  /** Fokus po wyniku: komunikat (sukces lub błąd ogólny) albo pole z błędem. */
  private focusResult(): void {
    if (this.sent() || this.formError()) {
      afterNextRender(() => this.resultMessage()?.nativeElement.focus(), {
        injector: this.injector,
      });
      return;
    }
    if (this.form.email().invalid()) {
      this.form.email().focusBoundControl();
    }
  }
}
