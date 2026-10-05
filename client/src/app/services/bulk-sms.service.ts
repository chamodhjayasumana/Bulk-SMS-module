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

export interface SmsGatewayStatus {
  online: boolean;
  networkAvailable: boolean;
  simOperator: string;
  ipAddress?: string | null;
  port?: number | null;
  error?: string | null;
  mode: string;
  sendingAllowed: boolean;
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

export interface CampaignDraftRequest {
  campaignDescription: string;
  language: 'en' | 'si' | 'ta' | string;
  senderName: string;
  maxSegments: number;
  tone: 'professional' | 'friendly' | 'urgent' | 'promotional' | string;
  includeCallToAction: boolean;
  personalizationFields: string[];
}

export interface CampaignSuggestion {
  message: string;
  language: string;
  characterCount: number;
  segments: number;
  tone: string;
  warnings: string[];
  placeholders: string[];
}

export interface CampaignDraft {
  suggestions: CampaignSuggestion[];
  safetyWarnings: string[];
  requiresReview: boolean;
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

  getGatewayStatus(): Observable<ApiResponse<SmsGatewayStatus>> {
    return this.http.get<ApiResponse<SmsGatewayStatus>>(`${environment.apiBaseUrl}/sms/gateway/status`);
  }

  sendTest(phoneNumber: string, message: string): Observable<ApiResponse<SmsRecipientResult>> {
    return this.http.post<ApiResponse<SmsRecipientResult>>(`${environment.apiBaseUrl}/sms/send-test`, {
      phoneNumber,
      message
    });
  }

  draftCampaign(request: CampaignDraftRequest): Observable<ApiResponse<CampaignDraft>> {
    return this.http.post<ApiResponse<CampaignDraft>>(`${environment.apiBaseUrl}/ai/campaign-draft`, request);
  }

  sendBulk(message: string, recipients: string[], confirmed: boolean): Observable<ApiResponse<BulkSendResult>> {
    return this.http.post<ApiResponse<BulkSendResult>>(`${environment.apiBaseUrl}/sms/send-bulk`, {
      message,
      recipients,
      confirmed
    });
  }
}
