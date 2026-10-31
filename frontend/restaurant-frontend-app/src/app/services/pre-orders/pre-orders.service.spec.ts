import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { DishListResponse } from '@app/models/dishes/dish-list-response.model';
import { Order } from '@app/models/orders/order.model';

import { PreOrdersService } from './pre-orders.service';

describe('PreOrdersService', () => {
  const baseUrl = 'https://test.api';
  const reservationId = 'reservation-1';

  const dish: DishListResponse = {
    id: 'dish-1',
    name: 'Lemon Sorbet',
    imageUrl: 'img.png',
    price: 6,
    state: 'Available',
    weight: '120 g',
    dishType: 'Dessert',
    description: '',
    calories: '',
    proteins: '',
    carbohydrates: '',
    fats: '',
    vitamins: '',
  };

  let service: PreOrdersService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: baseUrl },
      ],
    });
    service = TestBed.inject(PreOrdersService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  // Regression test for: adding a second dish to a reservation that already has an
  // order used to incorrectly POST /orders again (because the old implementation
  // decided create-vs-edit from GET /orders, which the backend scopes to only the
  // customer's single soonest upcoming reservation), and the backend rejected the
  // duplicate create with "Active order already exists for reservation {id}.".
  it('edits the existing order via PUT /orders/{reservationId} when one already exists for that reservation', () => {
    let result: Order | undefined;
    service.addDishToPreOrderByReservationId(reservationId, dish, 1).subscribe((order) => {
      result = order as unknown as Order;
    });

    const getReq = httpMock.expectOne(`${baseUrl}/orders/${reservationId}`);
    expect(getReq.request.method).toBe('GET');
    getReq.flush({
      orderId: 'order-1',
      reservationId,
      reservationAddress: 'Some street',
      reservationTime: 'today',
      orderItems: [],
      status: 0,
    } as Order);

    const putReq = httpMock.expectOne(`${baseUrl}/orders/${reservationId}`);
    expect(putReq.request.method).toBe('PUT');
    expect(putReq.request.body.orderItems).toEqual([{ dishId: dish.id, quantity: 1 }]);
    putReq.flush({});

    // Must never have attempted to create a new order when one already exists.
    httpMock.expectNone(`${baseUrl}/orders`);
  });

  it('creates a new order via POST /orders when none exists yet for that reservation (404)', () => {
    service.addDishToPreOrderByReservationId(reservationId, dish, 1).subscribe();

    const getReq = httpMock.expectOne(`${baseUrl}/orders/${reservationId}`);
    getReq.flush('Not found', { status: 404, statusText: 'Not Found' });

    const postReq = httpMock.expectOne(`${baseUrl}/orders`);
    expect(postReq.request.method).toBe('POST');
    expect(postReq.request.body).toEqual({
      reservationId,
      orderItems: [{ dishId: dish.id, quantity: 1 }],
    });
    postReq.flush({});
  });

  it('propagates a non-404 error from the per-reservation lookup instead of trying to create', () => {
    let errored = false;
    service.addDishToPreOrderByReservationId(reservationId, dish, 1).subscribe({
      error: () => (errored = true),
    });

    const getReq = httpMock.expectOne(`${baseUrl}/orders/${reservationId}`);
    getReq.flush('Server error', { status: 500, statusText: 'Internal Server Error' });

    expect(errored).toBeTrue();
    httpMock.expectNone(`${baseUrl}/orders`);
  });
});
