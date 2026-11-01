import { CanActivate, Router } from '@angular/router';
import { AuthenticationService } from '../services/authentication.service';
import { inject, Injectable } from '@angular/core';

// Guards the /reports route. Report generation/download is a Waiter-only feature
// (see US05 and GenerateReportHandler.cs, which requires RoleHelper.IsWaiter).
// This used to check for role === 'Admin', a role that is never actually assigned
// to any user in this system (only Customer and Waiter Cognito groups exist), which
// made /reports permanently unreachable through the UI for everyone, including the
// waiters it's meant for. Renamed from AdminGuard and fixed to check 'Waiter'.
@Injectable({
  providedIn: 'root',
})
export class WaiterReportsGuard implements CanActivate {
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
