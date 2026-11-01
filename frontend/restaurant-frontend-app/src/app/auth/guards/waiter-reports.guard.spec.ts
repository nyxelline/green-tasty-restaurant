import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';

import { WaiterReportsGuard } from './waiter-reports.guard';

describe('WaiterReportsGuard', () => {
  let guard: WaiterReportsGuard;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: 'https://test.api' },
      ],
    });
    guard = TestBed.inject(WaiterReportsGuard);
  });

  it('should be created', () => {
    expect(guard).toBeTruthy();
  });
});
