import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { routes } from '../app.routes';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';

describe('authGuard', () => {
  it('protects the bulk SMS page, including the AI assistant', () => {
    const route = routes.find((item) => item.path === 'bulk-sms');
    expect(route?.canActivate).toContain(authGuard);
  });

  it('sends a signed-out user to login', () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        { provide: AuthService, useValue: { isAuthenticated: signal(false) } }
      ]
    });

    const result = TestBed.runInInjectionContext(() => authGuard({} as never, {} as never));
    const url = TestBed.inject(Router).serializeUrl(result as UrlTree);
    expect(url).toBe('/login');
  });
});
