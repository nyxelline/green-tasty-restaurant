import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ToastService } from '../services/toast/toast.service';
import { Tokens } from './models/tokens.model';
import { AuthenticationService } from './services/authentication.service';
import { SKIP_ERROR_TOAST } from './skip-error-toast.context';
import { tokenInterceptor } from './token.interceptor';

const URL = 'https://test.api/reservations';

describe('tokenInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let toastService: jasmine.SpyObj<ToastService>;

  beforeEach(() => {
    toastService = jasmine.createSpyObj<ToastService>('ToastService', ['error']);
    const authService = jasmine.createSpyObj<AuthenticationService>('AuthenticationService', [
      'getTokens',
    ]);
    authService.getTokens.and.returnValue(null);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([tokenInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toastService },
        { provide: AuthenticationService, useValue: authService },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('shows the server message in a toast for a 400 response', () => {
    http.post(URL, {}).subscribe({ error: () => undefined });

    httpMock
      .expectOne(URL)
      .flush({ message: 'Overlaps' }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.error).toHaveBeenCalledWith('Overlaps', 'Error');
  });

  it('does not show a toast but still rethrows when SKIP_ERROR_TOAST is set', () => {
    let receivedStatus: number | undefined;
    http
      .post(URL, {}, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
      .subscribe({ error: (error) => (receivedStatus = error.status) });

    httpMock
      .expectOne(URL)
      .flush({ message: 'Overlaps' }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.error).not.toHaveBeenCalled();
    expect(receivedStatus).toBe(400);
  });
});

describe('tokenInterceptor and the /guest/ endpoints', () => {
  const OWNER_TOKEN: Tokens = { idToken: 'owner-id-token', accessToken: 'owner-access-token' };
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    const authService = jasmine.createSpyObj<AuthenticationService>('AuthenticationService', [
      'getTokens',
    ]);
    authService.getTokens.and.returnValue(OWNER_TOKEN);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([tokenInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: jasmine.createSpyObj<ToastService>('ToastService', ['error']) },
        { provide: AuthenticationService, useValue: authService },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('does not touch the authorization of guest session calls', () => {
    http.get('https://test.api/guest/sessions/guest-1/menu').subscribe();

    const req = httpMock.expectOne('https://test.api/guest/sessions/guest-1/menu');
    expect(req.request.headers.has('Authorization')).toBeFalse();
    req.flush({});
  });

  it('sends the owner token to the owner-only group sessions calls', () => {
    http.get('https://test.api/guest/groups/group-1/sessions').subscribe();

    const req = httpMock.expectOne('https://test.api/guest/groups/group-1/sessions');
    expect(req.request.headers.get('Authorization')).toBe('Bearer owner-id-token');
    req.flush({});
  });

  it('sends the owner token when the owner removes a guest dish', () => {
    http.delete('https://test.api/guest/groups/group-1/sessions/guest-1/dishes/dish-1').subscribe();

    const req = httpMock.expectOne(
      'https://test.api/guest/groups/group-1/sessions/guest-1/dishes/dish-1'
    );
    expect(req.request.headers.get('Authorization')).toBe('Bearer owner-id-token');
    req.flush({});
  });

  it('sends the owner token to the group bill call', () => {
    http.get('https://test.api/guest/groups/group-1/bill').subscribe();

    const req = httpMock.expectOne('https://test.api/guest/groups/group-1/bill');
    expect(req.request.headers.get('Authorization')).toBe('Bearer owner-id-token');
    req.flush({});
  });

  it('does not touch the authorization of the guest bill call', () => {
    http.get('https://test.api/guest/sessions/guest-1/bill').subscribe();

    const req = httpMock.expectOne('https://test.api/guest/sessions/guest-1/bill');
    expect(req.request.headers.has('Authorization')).toBeFalse();
    req.flush({});
  });

  it('keeps the group status call public like the other guest calls', () => {
    http.get('https://test.api/guest/groups/group-1/status').subscribe();

    const req = httpMock.expectOne('https://test.api/guest/groups/group-1/status');
    expect(req.request.headers.has('Authorization')).toBeFalse();
    req.flush({});
  });
});

