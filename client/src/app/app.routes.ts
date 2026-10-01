import { Routes } from '@angular/router';
import { authGuard } from './services/auth.guard';
import { LoginPage } from './pages/login/login';
import { BulkSmsPage } from './pages/bulk-sms/bulk-sms';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'bulk-sms' },
  { path: 'login', component: LoginPage },
  { path: 'bulk-sms', component: BulkSmsPage, canActivate: [authGuard] },
  { path: '**', redirectTo: 'bulk-sms' }
];
