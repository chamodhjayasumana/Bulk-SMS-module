import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';

interface ApiResponse<T> {
  success: boolean;
  message: string;
  data: T;
}

interface LoginData {
  token: string;
  accessType: string;
  userId: string;
  expiresAt: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly tokenKey = 'bulk_sms_token';
  readonly isAuthenticated = signal(!!localStorage.getItem(this.tokenKey));

  constructor(private http: HttpClient) {}

  login(username: string, password: string): Observable<ApiResponse<LoginData>> {
    return this.http
      .post<ApiResponse<LoginData>>(`${environment.apiBaseUrl}/Auth/token`, { username, password })
      .pipe(
        tap((res) => {
          if (res.success && res.data?.token) {
            localStorage.setItem(this.tokenKey, res.data.token);
            this.isAuthenticated.set(true);
          }
        })
      );
  }

  logout(): void {
    localStorage.removeItem(this.tokenKey);
    this.isAuthenticated.set(false);
  }

  getToken(): string | null {
    return localStorage.getItem(this.tokenKey);
  }
}
