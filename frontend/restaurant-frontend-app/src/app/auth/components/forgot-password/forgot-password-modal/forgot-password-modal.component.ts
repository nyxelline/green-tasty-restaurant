import { ChangeDetectionStrategy, Component, inject, output, signal } from '@angular/core';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';

import { AuthenticationService } from '@app/auth/services/authentication.service';
import { FormValidatorsService } from '@app/auth/services/form-validators-service.service';
import { ConfirmPasswordComponent } from '@app/shared/custom-components/confirm-password/confirm-password.component';
import { CustomButtonComponent } from '@app/shared/custom-components/custom-button/custom-button.component';
import { ModalComponent } from '@app/shared/custom-components/modal/modal.component';
import { PasswordWithRulesComponent } from '@app/shared/custom-components/password-with-rules/password-with-rules.component';

type Step = 'request' | 'reset' | 'done';

@Component({
  selector: 'app-forgot-password-modal',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    ModalComponent,
    CustomButtonComponent,
    PasswordWithRulesComponent,
    ConfirmPasswordComponent,
  ],
  templateUrl: './forgot-password-modal.component.html',
  styleUrl: './forgot-password-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ForgotPasswordModalComponent {
  readonly closed = output<void>();
  readonly passwordReset = output<string>();

  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthenticationService);
  private readonly formValidatorsService = inject(FormValidatorsService);

  readonly step = signal<Step>('request');
  readonly isLoading = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly requestForm = this.fb.group({
    email: [
      '',
      [Validators.required, Validators.pattern(/^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/)],
    ],
  });

  readonly resetForm = this.fb.group(
    {
      code: ['', Validators.required],
      password: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(16)]],
      confirmPassword: ['', Validators.required],
    },
    { validators: (control) => this.formValidatorsService.passwordMatchValidator(control) },
  );

  get email() {
    return this.requestForm.get('email');
  }

  get code() {
    return this.resetForm.get('code');
  }

  get password() {
    return this.resetForm.get('password') as FormControl;
  }

  get confirmPassword() {
    return this.resetForm.get('confirmPassword') as FormControl;
  }

  close(): void {
    this.closed.emit();
  }

  async onRequestSubmit(): Promise<void> {
    this.errorMessage.set(null);

    if (this.requestForm.invalid) {
      this.requestForm.markAllAsTouched();
      return;
    }

    this.isLoading.set(true);
    try {
      const { email } = this.requestForm.value;
      const result = await this.auth.forgotPassword(email!);

      if (!result.success) {
        this.errorMessage.set(result.message || 'Something went wrong. Please try again.');
        return;
      }

      this.step.set('reset');
    } finally {
      this.isLoading.set(false);
    }
  }

  async onResetSubmit(): Promise<void> {
    this.errorMessage.set(null);

    if (this.resetForm.invalid) {
      this.resetForm.markAllAsTouched();
      return;
    }

    this.isLoading.set(true);
    try {
      const { code, password, confirmPassword } = this.resetForm.value;
      const result = await this.auth.confirmForgotPassword(
        this.email!.value!,
        code!,
        password!,
        confirmPassword!,
      );

      if (!result.success) {
        this.errorMessage.set(result.message || 'Something went wrong. Please try again.');
        return;
      }

      this.step.set('done');
      this.passwordReset.emit(this.email!.value!);
    } finally {
      this.isLoading.set(false);
    }
  }

  backToRequestStep(): void {
    this.errorMessage.set(null);
    this.step.set('request');
  }
}
