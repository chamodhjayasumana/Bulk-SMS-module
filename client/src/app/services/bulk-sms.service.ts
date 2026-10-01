import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface ApiResponse<T> {
  success: boolean;
  message: string;
  data: T;
}

export interface MessageEstimate {
  characterCount: number;
  segmentCount: number;
  isUnicode: boolean;
  recipientCount: number;
  estimatedTotalSmsCount: number;
}

export interface ValidateNumbersResult {
  total: number;
  valid: number;
  invalid: number;
  duplicates: number;
  numbers: string[];
  invalidSamples: { rawValue: string; reason: string }[];
  estimate: MessageEstimate;
}

export interface SmsRecipientResult {
  mobileNumber: string;
  status: 'Pending' | 'Sent' | 'Failed' | number;
  providerMessageId?: string;
  errorMessage?: string;
  timestamp: string;
}

export interface BulkSendResult {
  total: number;
  successful: number;
  failed: number;
  results: SmsRecipientResult[];
}

@Injectable({ providedIn: 'root' })
export class BulkSmsService {
  constructor(private http: HttpClient) {}

  validate(file: File, message: string): Observable<ApiResponse<ValidateNumbersResult>> {
    const form = new FormData();
    form.append('file', file);
    form.append('message', message ?? '');
    return this.http.post<ApiResponse<ValidateNumbersResult>>(`${environment.apiBaseUrl}/sms/validate`, form);
  }

  sendBulk(message: string, recipients: string[], confirmed: boolean): Observable<ApiResponse<BulkSendResult>> {
    return this.http.post<ApiResponse<BulkSendResult>>(`${environment.apiBaseUrl}/sms/send-bulk`, {
      message,
      recipients,
      confirmed
    });
  }
}
