import { Component, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import {
  BulkSendResult,
  BulkSmsService,
  SmsRecipientResult,
  ValidateNumbersResult
} from '../../services/bulk-sms.service';

@Component({
  selector: 'app-bulk-sms',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './bulk-sms.html',
  styleUrl: './bulk-sms.css'
})
export class BulkSmsPage {
  message = '';
  selectedFile: File | null = null;
  validation = signal<ValidateNumbersResult | null>(null);
  sendResult = signal<BulkSendResult | null>(null);
  error = signal('');
  info = signal('');
  validating = signal(false);
  sending = signal(false);
  progress = signal(0);

  readonly characterCount = computed(() => this.message.length);
  readonly isUnicode = computed(() => /[^\u0000-\u007F]/.test(this.message) || this.containsNonGsm(this.message));
  readonly segmentCount = computed(() => this.estimateSegments(this.message));
  readonly recipientCount = computed(() => this.validation()?.valid ?? 0);
  readonly estimatedTotal = computed(() => this.segmentCount() * this.recipientCount());
  readonly canSend = computed(() => !!this.validation() && this.validation()!.valid > 0 && !!this.message.trim() && !this.sending());

  readonly successful = computed(() => this.sendResult()?.successful ?? 0);
  readonly failed = computed(() => this.sendResult()?.failed ?? 0);
  readonly pending = computed(() => {
    const r = this.sendResult();
    if (!r) return 0;
    return r.results.filter((x) => this.statusLabel(x.status) === 'Pending').length;
  });

  constructor(
    private bulkSms: BulkSmsService,
    private auth: AuthService,
    private router: Router
  ) {}

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.[0] ?? null;
    this.validation.set(null);
    this.sendResult.set(null);
    this.error.set('');
    this.info.set('');
  }

  validate(): void {
    if (!this.selectedFile) {
      this.error.set('Choose a CSV or XLSX file first.');
      return;
    }

    this.error.set('');
    this.info.set('');
    this.validating.set(true);
    this.sendResult.set(null);

    this.bulkSms.validate(this.selectedFile, this.message).subscribe({
      next: (res) => {
        this.validating.set(false);
        if (!res.success) {
          this.error.set(res.message || 'Validation failed');
          return;
        }
        this.validation.set(res.data);
        this.info.set(`Validated ${res.data.valid} recipients.`);
      },
      error: (err) => {
        this.validating.set(false);
        this.error.set(err?.error?.message || 'Validation failed');
      }
    });
  }

  send(): void {
    const v = this.validation();
    if (!v || !this.message.trim()) return;

    const large = v.valid >= 100;
    if (large && !confirm(`Send SMS to ${v.valid} recipients?`)) {
      return;
    }

    this.error.set('');
    this.info.set('');
    this.sending.set(true);
    this.progress.set(15);

    const timer = setInterval(() => {
      this.progress.update((p) => (p < 90 ? p + 5 : p));
    }, 400);

    this.bulkSms.sendBulk(this.message, v.numbers, true).subscribe({
      next: (res) => {
        clearInterval(timer);
        this.sending.set(false);
        this.progress.set(100);
        if (!res.success) {
          this.error.set(res.message || 'Send failed');
          return;
        }
        this.sendResult.set(res.data);
        this.info.set(`Completed: ${res.data.successful} sent, ${res.data.failed} failed.`);
      },
      error: (err) => {
        clearInterval(timer);
        this.sending.set(false);
        this.progress.set(0);
        this.error.set(err?.error?.message || 'Send failed');
      }
    });
  }

  exportFailed(): void {
    const failed = (this.sendResult()?.results ?? []).filter((r) => this.statusLabel(r.status) === 'Failed');
    if (!failed.length) return;

    const lines = ['MobileNumber,Error', ...failed.map((r) => `${r.mobileNumber},"${(r.errorMessage || '').replace(/"/g, '""')}"`)];
    const blob = new Blob([lines.join('\n')], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `failed-numbers-${Date.now()}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  logout(): void {
    this.auth.logout();
    this.router.navigateByUrl('/login');
  }

  statusLabel(status: SmsRecipientResult['status']): string {
    if (typeof status === 'number') {
      return ['Pending', 'Sent', 'Failed'][status] ?? String(status);
    }
    return status;
  }

  private estimateSegments(message: string): number {
    if (!message) return 0;
    const unicode = this.isUnicode();
    if (!unicode) {
      const units = this.countGsmSeptets(message);
      if (units <= 160) return 1;
      return Math.ceil(units / 153);
    }
    if (message.length <= 70) return 1;
    return Math.ceil(message.length / 67);
  }

  private countGsmSeptets(message: string): number {
    const extended = new Set(['^', '{', '}', '\\', '[', '~', ']', '|', '€']);
    let count = 0;
    for (const c of message) count += extended.has(c) ? 2 : 1;
    return count;
  }

  private containsNonGsm(message: string): boolean {
    const gsm =
      '@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !"#¤%&\'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà^{}\\[~]|€';
    for (const c of message) {
      if (!gsm.includes(c)) return true;
    }
    return false;
  }
}
