import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ReservationCustomer } from '@app/models/reservations/reservation.model';
import { CardReservationComponent } from './card-reservation.component';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';

describe('CardReservationComponent', () => {
  let component: CardReservationComponent;
  let fixture: ComponentFixture<CardReservationComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [{ provide: API_BASE_URL, useValue: 'https://test.api' }],
      imports: [CardReservationComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(CardReservationComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

describe('CardReservationComponent group order', () => {
  let component: CardReservationComponent;
  let fixture: ComponentFixture<CardReservationComponent>;

  const reservation = (guestCount: number): ReservationCustomer =>
    ({ id: 'res-1', guestCount }) as ReservationCustomer;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [{ provide: API_BASE_URL, useValue: 'https://test.api' }],
      imports: [CardReservationComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(CardReservationComponent);
    component = fixture.componentInstance;
  });

  it('offers the group order only for reservations with two or more guests', () => {
    fixture.componentRef.setInput('r', reservation(1));
    expect(component.canOpenGroupOrder()).toBeFalse();

    fixture.componentRef.setInput('r', reservation(2));
    expect(component.canOpenGroupOrder()).toBeTrue();
  });

  it('opens the group order page of the reservation', () => {
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    fixture.componentRef.setInput('r', reservation(4));

    component.onGroupOrder();

    expect(navigate).toHaveBeenCalledWith(['/group-order', 'res-1']);
  });
});
