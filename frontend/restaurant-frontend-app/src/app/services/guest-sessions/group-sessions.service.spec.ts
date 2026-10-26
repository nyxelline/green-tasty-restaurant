import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { GroupBill } from '@app/models/group-order/group-bill.model';
import { GroupSessionsApiResponse } from '@app/models/group-order/group-sessions-api.model';
import { GroupSessions } from '@app/models/group-order/group-sessions.model';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';

import { GroupSessionsService } from './group-sessions.service';

const BASE_URL = 'https://test.api';
const GROUP_URL = `${BASE_URL}/guest/groups/group-1/sessions`;

const API_RESPONSE: GroupSessionsApiResponse = {
  Participants: [
    {
      Id: 'owner-1',
      Type: 'Owner',
      Name: 'Owner',
      Total: 10,
      IsSubmitted: false,
      Items: [
        {
          quantity: 1,
          dish: {
            id: 'dish-1',
            name: 'Soup',
            description: '',
            price: 10,
            imageUrl: null,
            state: 'Available',
          },
        },
      ],
    },
    {
      Id: 'guest-1',
      Type: 'Guest',
      Name: 'Anna',
      Total: 24,
      IsSubmitted: true,
      Items: [
        {
          quantity: 2,
          dish: {
            id: 'dish-2',
            name: 'Salad',
            description: '',
            price: 12,
            imageUrl: 'https://img/salad.jpg',
            state: 'On Stop',
          },
        },
      ],
    },
  ],
  Total: 34,
};

describe('GroupSessionsService', () => {
  let service: GroupSessionsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: BASE_URL },
      ],
    });
    service = TestBed.inject(GroupSessionsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads the group sessions with GET and maps them to camelCase', () => {
    let result: GroupSessions | undefined;

    service.getSessions$('group-1').subscribe((sessions) => (result = sessions));

    const req = httpMock.expectOne(GROUP_URL);
    expect(req.request.method).toBe('GET');
    req.flush(API_RESPONSE);

    expect(result?.total).toBe(34);
    expect(result?.participants.map((participant) => participant.type)).toEqual(['Owner', 'Guest']);
    expect(result?.participants[1]).toEqual({
      id: 'guest-1',
      type: 'Guest',
      name: 'Anna',
      total: 24,
      isSubmitted: true,
      items: [
        {
          quantity: 2,
          dish: {
            id: 'dish-2',
            name: 'Salad',
            price: 12,
            imageUrl: 'https://img/salad.jpg',
            isAvailable: false,
          },
        },
      ],
    });
  });

  it('treats a dish that is "Available" as available', () => {
    let result: GroupSessions | undefined;

    service.getSessions$('group-1').subscribe((sessions) => (result = sessions));
    httpMock.expectOne(GROUP_URL).flush(API_RESPONSE);

    expect(result?.participants[0].items[0].dish.isAvailable).toBeTrue();
  });

  it('returns empty lists when the server sends no participants or items', () => {
    let result: GroupSessions | undefined;

    service.getSessions$('group-1').subscribe((sessions) => (result = sessions));
    httpMock.expectOne(GROUP_URL).flush({ Participants: null, Total: 0 });

    expect(result).toEqual({ participants: [], total: 0 });
  });

  it('loads the split bill of the group with GET', () => {
    const bill: GroupBill = {
      groupReservationId: 'group-1',
      guests: [
        {
          guestSessionId: 'guest-1',
          name: 'Anna',
          isOwner: false,
          isSubmitted: true,
          lines: [{ dishId: 'dish-1', name: 'Soup', unitPrice: 10, quantity: 2, lineTotal: 20 }],
          subtotal: 20,
          sharePercent: 100,
        },
      ],
      total: 20,
    };
    let result: GroupBill | undefined;

    service.getBill$('group-1').subscribe((response) => (result = response));

    const req = httpMock.expectOne(`${BASE_URL}/guest/groups/group-1/bill`);
    expect(req.request.method).toBe('GET');
    req.flush(bill);

    expect(result).toEqual(bill);
  });

  it('removes a guest with DELETE on the guest session', () => {
    service.removeGuest$('group-1', 'guest-1').subscribe();

    const req = httpMock.expectOne(`${GROUP_URL}/guest-1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(API_RESPONSE);
  });

  it('removes a dish with DELETE on the dish of the guest session', () => {
    let result: GroupSessions | undefined;

    service.removeDish$('group-1', 'guest-1', 'dish-2').subscribe((sessions) => (result = sessions));

    const req = httpMock.expectOne(`${GROUP_URL}/guest-1/dishes/dish-2`);
    expect(req.request.method).toBe('DELETE');
    req.flush(API_RESPONSE);
    expect(result?.participants.length).toBe(2);
  });

  it('encodes path parameters', () => {
    service.removeDish$('gro/up', 'gu est', 'di?sh').subscribe();

    const req = httpMock.expectOne(
      `${BASE_URL}/guest/groups/gro%2Fup/sessions/gu%20est/dishes/di%3Fsh`
    );
    expect(req.request.method).toBe('DELETE');
    req.flush(API_RESPONSE);
  });

  it('propagates HTTP errors', () => {
    let status: number | undefined;

    service.getSessions$('group-1').subscribe({ error: (error) => (status = error.status) });

    httpMock.expectOne(GROUP_URL).flush({ error: 'forbidden' }, { status: 403, statusText: 'Forbidden' });
    expect(status).toBe(403);
  });
});
