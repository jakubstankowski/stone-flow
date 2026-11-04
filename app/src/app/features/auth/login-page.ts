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
import { FormField, TreeValidationResult, form, required, submit } from '@angular/forms/signals';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthSession } from '../../core/auth/auth-session';
import { safeReturnUrl } from '../../core/auth/return-url';
import { Button } from '../../shared/ui/button';
import { Field } from '../../shared/ui/field';
import {
  INVALID_CREDENTIALS_MESSAGE,
  VALIDATION_MESSAGES,
  formErrorMessage,
  noticeView,
  serverFieldErrors,
  visibleError,
} from './auth-messages';

interface LoginModel {
  email: string;
  password: string;
}

/** Ekran `/logowanie` (spec, punkty 4–9, 12, 13, 19, 30). */
@Component({
  selector: 'app-login-page',
  imports: [Button, Field, FormField, RouterLink],
  templateUrl: './login-page.html',
})
export class LoginPage {
  private readonly session = inject(AuthSession);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly injector = inject(Injector);

  protected readonly notice = noticeView(this.session.takeNotice());
  protected readonly formError = signal<string | null>(null);

  private readonly model = signal<LoginModel>({ email: '', password: '' });
  protected readonly form = form(this.model, (path) => {
    required(path.email, { message: VALIDATION_MESSAGES.emailRequired });
    required(path.password, { message: VALIDATION_MESSAGES.passwordRequired });
  });
  protected readonly busy = computed(() => this.form().submitting());
  protected readonly emailError = computed(() => visibleError(this.form.email));
  protected readonly passwordError = computed(() => visibleError(this.form.password));

  private readonly formMessage = viewChild<ElementRef<HTMLElement>>('formMessage');

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    this.formError.set(null);
    await submit(this.form, () => this.login());
    this.focusResult();
  }

  private async login(): Promise<TreeValidationResult> {
    const { email, password } = this.model();
    const error = await this.session.login(email.trim(), password);
    if (!error) {
      await this.router.navigateByUrl(
        safeReturnUrl(this.route.snapshot.queryParamMap.get('powrot')),
      );
      return null;
    }
    this.form.password().reset('');
    if (error.kind === 'validation') {
      const fieldErrors = serverFieldErrors(error, {
        email: this.form.email,
        password: this.form.password,
      });
      if (fieldErrors.length > 0) {
        return fieldErrors;
      }
    }
    this.formError.set(
      error.kind === 'problem' && error.status === 401
        ? INVALID_CREDENTIALS_MESSAGE
        : formErrorMessage(error),
    );
    return null;
  }

  /** Fokus po wyniku: komunikat ogólny albo pierwsze pole z błędem. */
  private focusResult(): void {
    if (this.formError()) {
      afterNextRender(() => this.formMessage()?.nativeElement.focus(), {
        injector: this.injector,
      });
      return;
    }
    const firstInvalid = [this.form.email, this.form.password].find((field) => field().invalid());
    firstInvalid?.().focusBoundControl();
  }
}
