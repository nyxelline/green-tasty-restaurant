import { CanActivate, Router } from '@angular/router';
import { AuthenticationService } from '../services/authentication.service';
import { inject, Injectable } from '@angular/core';

// Guards all Waiter-only routes (/reports, /waiter-reservations, /waiter-pre-order).
// These were previously protected inconsistently: /reports required a nonexistent
// 'Admin' role (making it unreachable for anyone), /waiter-reservations only checked
// AuthGuard (any logged-in user, including Customers), and /waiter-pre-order had no
// guard at all. Consolidated here so every waiter-facing page requires role 'Waiter'.
@Injectable({
  providedIn: 'root',
})
export class WaiterGuard implements CanActivate {
  private authService = inject(AuthenticationService);
  private router = inject(Router);

  canActivate(): boolean {
    const user = this.authService.getUser();

    if (!user || !user.role) {
      console.warn('User not authenticated or missing role');
      this.router.navigate(['/main']);
      return false;
    }

    if (user.role === 'Waiter') {
      return true;
    }

    this.router.navigate(['/main']);
    return false;
  }
}
