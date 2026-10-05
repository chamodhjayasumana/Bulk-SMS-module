import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { BulkSmsService } from '../../services/bulk-sms.service';
import { BulkSmsPage } from './bulk-sms';

describe('BulkSmsPage AI assistant', () => {
  let fixture: ComponentFixture<BulkSmsPage>;
  let draft: jasmine.Spy;

  beforeEach(async () => {
    draft = jasmine.createSpy('draftCampaign');
    await TestBed.configureTestingModule({
      imports: [BulkSmsPage],
      providers: [
        provideRouter([]),
        {
          provide: BulkSmsService,
          useValue: {
            getGatewayStatus: () =>
              of({
                success: true,
                message: 'OK',
                data: {
                  online: false,
                  networkAvailable: false,
                  simOperator: '',
                  mode: 'Mock',
                  sendingAllowed: true
                }
              }),
            draftCampaign: draft
          }
        },
        { provide: AuthService, useValue: { logout: () => {}, isAuthenticated: signal(true) } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(BulkSmsPage);
    fixture.detectChanges();
  });

  it('renders the assistant panel', () => {
    const panel = fixture.nativeElement.querySelector('[data-ai="panel"]') as HTMLElement;
    expect(panel.textContent).toContain('AI Campaign Assistant');
    expect(panel.textContent).toContain('never sends SMS');
  });

  it('requires a campaign description', () => {
    clickGenerate();
    fixture.detectChanges();
    expect(draft).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[data-ai="error"]').textContent).toContain('Describe the campaign');
  });

  it('shows a loading state while generating', () => {
    draft.and.returnValue(new Subject());
    setDescription('Promote our weekend discount');
    clickGenerate();
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('[data-ai="generate"]') as HTMLButtonElement;
    expect(button.disabled).toBeTrue();
    expect(button.textContent).toContain('Generating…');
  });

  it('shows suggestions and warnings', () => {
    draft.and.returnValue(of({
      success: true,
      message: 'Review these suggestions before sending.',
      data: {
        requiresReview: true,
        safetyWarnings: ['Message looks like spam.'],
        suggestions: [
          {
            message: 'Hello {name}. Weekend discount.',
            language: 'en',
            characterCount: 32,
            segments: 1,
            tone: 'professional',
            warnings: ['Placeholder needs review.'],
            placeholders: ['name']
          }
        ]
      }
    }));
    setDescription('Promote our weekend discount');
    clickGenerate();
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Hello {name}. Weekend discount.');
    expect(text).toContain('Characters: 32');
    expect(text).toContain('SMS segments: 1');
    expect(text).toContain('Message looks like spam.');
    expect(text).toContain('Placeholder needs review.');
  });

  it('copies a suggestion into the SMS editor', () => {
    draft.and.returnValue(of({
      success: true,
      message: 'OK',
      data: {
        requiresReview: true,
        safetyWarnings: [],
        suggestions: [
          {
            message: 'Shop opens Saturday.',
            language: 'en',
            characterCount: 20,
            segments: 1,
            tone: 'friendly',
            warnings: [],
            placeholders: []
          }
        ]
      }
    }));
    setDescription('Opening hours');
    clickGenerate();
    fixture.detectChanges();
    fixture.debugElement.query(By.css('[data-ai="use"]')).triggerEventHandler('click', null);
    fixture.detectChanges();

    expect(fixture.componentInstance.message).toBe('Shop opens Saturday.');
    expect(fixture.nativeElement.textContent).toContain('Review it, then confirm before sending.');
  });

  it('shows API errors', () => {
    draft.and.returnValue(throwError(() => ({ error: { message: 'The AI provider timed out.' } })));
    setDescription('Promote our weekend discount');
    clickGenerate();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-ai="error"]').textContent).toContain('timed out');
  });

  function setDescription(value: string): void {
    fixture.componentInstance.aiDescription = value;
    fixture.detectChanges();
  }

  function clickGenerate(): void {
    (fixture.nativeElement.querySelector('[data-ai="generate"]') as HTMLButtonElement).click();
  }
});
