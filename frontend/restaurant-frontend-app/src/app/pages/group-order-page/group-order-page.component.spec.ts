import { CurrencyPipe } from '@angular/common';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';

import {
  GroupOrderDish,
  GroupParticipant,
  GroupSessions,
} from '@app/models/group-order/group-sessions.model';
import { GroupBill, GuestBill } from '@app/models/group-order/group-bill.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupSessionsService } from '@app/services/guest-sessions/group-sessions.service';
import { PreOrdersService } from '@app/services/pre-orders/pre-orders.service';
import { GuestInviteService } from '@app/services/reservation/guest-invite.service';
import { ToastService } from '@app/services/toast/toast.service';

import { GroupOrderPageComponent, REMOVAL_UNDO_DELAY_MS } from './group-order-page.component';

const dish = (id: string, name: string, price: number, isAvailable = true): GroupOrderDish => ({
  id,
  name,
  price,
  imageUrl: null,
  isAvailable,
});

const participant = (
  id: string,
  type: GroupParticipant['type'],
  name: string,
  isSubmitted: boolean,
  dishes: GroupOrderDish[]
): GroupParticipant => ({
  id,
  type,
  name,
  isSubmitted,
  items: dishes.map((item) => ({ dish: item, quantity: 1 })),
  total: dishes.reduce((sum, item) => sum + item.price, 0),
});

const pasta = dish('dish-pasta', 'Pasta', 10);
const salad = dish('dish-salad', 'Salad', 12);
const soup = dish('dish-soup', 'Soup', 8);
const tea = dish('dish-tea', 'Tea', 4);

const owner = participant('owner-1', 'Owner', 'Owner', false, [pasta]);
const anna = participant('guest-anna', 'Guest', 'Anna', true, [salad, soup]);
const max = participant('guest-max', 'Guest', 'Max', false, [tea]);
const olena = participant('guest-olena', 'Guest', 'Olena', false, []);

const billOf = (...guests: GuestBill[]): GroupBill => ({
  groupReservationId: 'group-1',
  guests,
  total: guests.reduce((sum, guest) => sum + guest.subtotal, 0),
});

const guestBill = (
  name: string,
  isOwner: boolean,
  isSubmitted: boolean,
  subtotal: number,
  sharePercent: number
): GuestBill => ({
  guestSessionId: `id-${name}`,
  name,
  isOwner,
  isSubmitted,
  lines: subtotal > 0 ? [{ dishId: 'd', name: 'Soup', unitPrice: subtotal, quantity: 1, lineTotal: subtotal }] : [],
  subtotal,
  sharePercent,
});

const sessionsOf = (...participants: GroupParticipant[]): GroupSessions => ({
  participants,
  total: participants.reduce((sum, entry) => sum + entry.total, 0),
});

describe('GroupOrderPageComponent', () => {
  let fixture: ComponentFixture<GroupOrderPageComponent>;
  let component: GroupOrderPageComponent;

  let groupSessionsService: jasmine.SpyObj<GroupSessionsService>;
  let guestInviteService: jasmine.SpyObj<GuestInviteService>;
  let preOrdersService: jasmine.SpyObj<PreOrdersService>;
  let toastService: jasmine.SpyObj<ToastService>;
  let router: jasmine.SpyObj<Router>;

  const text = (): string => (fixture.nativeElement as HTMLElement).textContent ?? '';

  async function createPage(initial: GroupSessions): Promise<void> {
    groupSessionsService.getSessions$.and.returnValue(of(initial));

    fixture = TestBed.createComponent(GroupOrderPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    groupSessionsService = jasmine.createSpyObj<GroupSessionsService>('GroupSessionsService', [
      'getSessions$',
      'removeGuest$',
      'removeDish$',
      'getBill$',
    ]);
    guestInviteService = jasmine.createSpyObj<GuestInviteService>('GuestInviteService', [
      'getInviteToken$',
      'getInvite$',
    ]);
    preOrdersService = jasmine.createSpyObj<PreOrdersService>('PreOrdersService', [
      'submitPreOrderByReservationId',
    ]);
    toastService = jasmine.createSpyObj<ToastService>('ToastService', ['success', 'error', 'info']);
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.resolveTo(true);

    groupSessionsService.getBill$.and.returnValue(
      of(billOf(guestBill('Owner', true, true, 10, 100)))
    );
    guestInviteService.getInviteToken$.and.returnValue(of({ token: 'tok-1' }));
    guestInviteService.getInvite$.and.returnValue(
      of({ GroupReservationId: 'group-1' } as GuestInvite)
    );

    await TestBed.configureTestingModule({
      imports: [GroupOrderPageComponent],
      providers: [
        { provide: GroupSessionsService, useValue: groupSessionsService },
        { provide: GuestInviteService, useValue: guestInviteService },
        { provide: PreOrdersService, useValue: preOrdersService },
        { provide: ToastService, useValue: toastService },
        { provide: Router, useValue: router },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ reservationId: 'res-1' }) } },
        },
      ],
    })
      .overrideComponent(GroupOrderPageComponent, {
        set: { imports: [CurrencyPipe], schemas: [NO_ERRORS_SCHEMA] },
      })
      .compileComponents();
  });

  describe('loading', () => {
    it('resolves the group reservation from the reservation invite and loads its sessions', async () => {
      await createPage(sessionsOf(owner, anna));

      expect(guestInviteService.getInviteToken$).toHaveBeenCalledWith('res-1');
      expect(guestInviteService.getInvite$).toHaveBeenCalledWith('tok-1');
      expect(groupSessionsService.getSessions$).toHaveBeenCalledWith('group-1');
      expect(component.loadState()).toBe('ready');
      expect(component.inviteLink()).toBe(`${window.location.origin}/join/tok-1`);
    });

    it('shows an error state with a retry when loading fails', async () => {
      groupSessionsService.getSessions$.and.returnValue(throwError(() => new Error('boom')));

      fixture = TestBed.createComponent(GroupOrderPageComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();

      expect(component.loadState()).toBe('error');
      expect(text()).toContain('We could not load the group order.');
    });
  });

  describe('state: waiting guests', () => {
    beforeEach(async () => createPage(sessionsOf(owner, anna, max, olena)));

    it('warns about the guests who have not finished', () => {
      expect(component.banner()).toEqual({
        kind: 'warn',
        text: 'Max is still choosing · Olena has no order yet',
      });
    });

    it('counts only submitted guests in the table total', () => {
      expect(component.ownerTotal()).toBe(10);
      expect(component.submittedGuestsTotal()).toBe(20);
      expect(component.tableTotal()).toBe(30);
    });

    it('offers to submit without the guests who are not ready', () => {
      expect(component.submitLabel()).toBe('Submit without Max and Olena');
    });

    it('expands submitted guests and collapses the others by default', () => {
      const views = component.guestViews();

      expect(views.find((guest) => guest.name === 'Anna')?.isExpanded).toBeTrue();
      expect(views.find((guest) => guest.name === 'Max')?.isExpanded).toBeFalse();
    });

    it('labels every guest with the right status', () => {
      const labels = component.guestViews().map((guest) => guest.statusLabel);

      expect(labels).toEqual(['Confirmed', 'Still choosing', 'No order yet']);
    });
  });

  describe('state: everybody is ready', () => {
    it('uses the plain submit label and shows no banner', async () => {
      await createPage(sessionsOf(owner, anna));

      expect(component.submitLabel()).toBe('Submit Pre-Order');
      expect(component.banner()).toBeNull();
      expect(component.canSubmit()).toBeTrue();
    });
  });

  describe('state: unavailable dish', () => {
    beforeEach(async () => {
      const onStop = dish('dish-risotto', 'Risotto', 15, false);
      await createPage(
        sessionsOf(owner, participant('guest-anna', 'Guest', 'Anna', true, [onStop, soup]))
      );
    });

    it('asks the owner to fix the order before submitting', () => {
      expect(component.banner()).toEqual({
        kind: 'error',
        text: '1 item needs your attention before you can submit',
      });
      expect(component.guestViews()[0].statusKind).toBe('attention');
    });

    it('blocks submitting', () => {
      expect(component.canSubmit()).toBeFalse();

      component.submit();

      expect(preOrdersService.submitPreOrderByReservationId).not.toHaveBeenCalled();
    });
  });

  describe('state: no guests yet', () => {
    beforeEach(async () => createPage(sessionsOf(owner)));

    it('shows an informational banner and the empty state with the invite action', () => {
      expect(component.banner()).toEqual({ kind: 'info', text: 'Group pre-order · just you so far' });
      expect(text()).toContain('No guests have joined yet');
      expect(text()).toContain('Copy invite link');
    });

    it('can submit the owner order alone', () => {
      expect(component.canSubmit()).toBeTrue();
      expect(component.tableTotal()).toBe(10);
    });
  });

  describe('removing a dish', () => {
    beforeEach(async () => createPage(sessionsOf(owner, anna)));

    it('hides the dish from the totals right away and marks the guest as changed', () => {
      component.removeDish('guest-anna', 'dish-salad');

      const guest = component.guestViews()[0];
      expect(guest.total).toBe(8);
      expect(guest.rows.find((row) => row.dishId === 'dish-salad')?.isRemoved).toBeTrue();
      expect(guest.statusKind).toBe('changed');
      expect(component.tableTotal()).toBe(18);

      component.undoRemoval('guest-anna', 'dish-salad');
    });

    it('removes the dish on the server once the undo window is over', fakeAsync(() => {
      const updated = sessionsOf(owner, participant('guest-anna', 'Guest', 'Anna', true, [soup]));
      groupSessionsService.removeDish$.and.returnValue(of(updated));

      component.removeDish('guest-anna', 'dish-salad');
      tick(REMOVAL_UNDO_DELAY_MS - 1);
      expect(groupSessionsService.removeDish$).not.toHaveBeenCalled();

      tick(1);

      expect(groupSessionsService.removeDish$).toHaveBeenCalledOnceWith(
        'group-1',
        'guest-anna',
        'dish-salad'
      );
      expect(component.sessions()).toEqual(updated);
      expect(component.pendingRemovals()).toEqual([]);
      expect(component.guestViews()[0].statusKind).toBe('changed');
    }));

    it('keeps the dish and never calls the server when the removal is undone', fakeAsync(() => {
      component.removeDish('guest-anna', 'dish-salad');
      component.undoRemoval('guest-anna', 'dish-salad');
      tick(REMOVAL_UNDO_DELAY_MS * 2);

      expect(groupSessionsService.removeDish$).not.toHaveBeenCalled();
      expect(component.guestViews()[0].total).toBe(20);
      expect(component.guestViews()[0].statusKind).toBe('confirmed');
    }));

    it('blocks submitting while a removal can still be undone', fakeAsync(() => {
      component.removeDish('guest-anna', 'dish-salad');

      expect(component.canSubmit()).toBeFalse();

      component.undoRemoval('guest-anna', 'dish-salad');
      expect(component.canSubmit()).toBeTrue();
    }));

    it('ignores a second removal of the same dish', fakeAsync(() => {
      groupSessionsService.removeDish$.and.returnValue(of(sessionsOf(owner)));

      component.removeDish('guest-anna', 'dish-salad');
      component.removeDish('guest-anna', 'dish-salad');
      tick(REMOVAL_UNDO_DELAY_MS);

      expect(groupSessionsService.removeDish$).toHaveBeenCalledTimes(1);
    }));

    it('restores the dish when the server rejects the removal', fakeAsync(() => {
      groupSessionsService.removeDish$.and.returnValue(throwError(() => new Error('forbidden')));

      component.removeDish('guest-anna', 'dish-salad');
      tick(REMOVAL_UNDO_DELAY_MS);

      expect(component.pendingRemovals()).toEqual([]);
      expect(component.guestViews()[0].total).toBe(20);
    }));

    it('still removes the dish when the owner leaves the page during the undo window', fakeAsync(() => {
      groupSessionsService.removeDish$.and.returnValue(of(sessionsOf(owner)));

      component.removeDish('guest-anna', 'dish-salad');
      fixture.destroy();

      expect(groupSessionsService.removeDish$).toHaveBeenCalledOnceWith(
        'group-1',
        'guest-anna',
        'dish-salad'
      );
    }));
  });

  describe('removing a guest', () => {
    beforeEach(async () => createPage(sessionsOf(owner, anna, max)));

    it('asks for confirmation first and can be cancelled', () => {
      component.askToRemoveGuest('guest-max');

      const confirming = component.guestViews().find((guest) => guest.id === 'guest-max');
      expect(confirming?.isConfirmingRemoval).toBeTrue();

      component.cancelRemoveGuest();

      expect(component.confirmingGuestId()).toBeNull();
      expect(groupSessionsService.removeGuest$).not.toHaveBeenCalled();
    });

    it('removes the guest after confirmation and shows the updated order', () => {
      const updated = sessionsOf(owner, anna);
      groupSessionsService.removeGuest$.and.returnValue(of(updated));
      component.askToRemoveGuest('guest-max');

      component.confirmRemoveGuest('guest-max');

      expect(groupSessionsService.removeGuest$).toHaveBeenCalledOnceWith('group-1', 'guest-max');
      expect(component.sessions()).toEqual(updated);
      expect(component.confirmingGuestId()).toBeNull();
      expect(toastService.success).toHaveBeenCalled();
    });
  });

  describe('split bill', () => {
    const everyone = billOf(
      guestBill('Owner', true, true, 10, 33.3),
      guestBill('Anna', false, true, 20, 66.7)
    );

    it('loads the bill of the group together with the sessions', async () => {
      groupSessionsService.getBill$.and.returnValue(of(everyone));

      await createPage(sessionsOf(owner, anna));

      expect(groupSessionsService.getBill$).toHaveBeenCalledWith('group-1');
      expect(component.billRows().map((row) => row.name)).toEqual(['Owner', 'Anna']);
      expect(component.billRows()[1].summary).toBe('Soup ×1');
    });

    it('shows the bill when somebody besides the owner has ordered', async () => {
      groupSessionsService.getBill$.and.returnValue(of(everyone));

      await createPage(sessionsOf(owner, anna));

      expect(component.showBill()).toBeTrue();
      expect(text()).toContain('Split bill');
      expect(text()).toContain('66.7%');
    });

    it('hides the bill while the owner is alone', async () => {
      await createPage(sessionsOf(owner));

      expect(component.showBill()).toBeFalse();
      expect(text()).not.toContain('Split bill');
    });

    it('calls the bill "so far" while a guest is still choosing and "total" afterwards', async () => {
      groupSessionsService.getBill$.and.returnValue(
        of(billOf(guestBill('Owner', true, true, 10, 50), guestBill('Max', false, false, 10, 50)))
      );
      await createPage(sessionsOf(owner, max));
      expect(component.billTitle()).toBe('Bill so far');
      expect(text()).toContain('Still choosing');

      groupSessionsService.getBill$.and.returnValue(of(everyone));
      component.load();
      expect(component.billTitle()).toBe('Bill total');
    });

    it('loads the bill again after a guest is removed', async () => {
      await createPage(sessionsOf(owner, anna));
      groupSessionsService.getBill$.calls.reset();
      groupSessionsService.removeGuest$.and.returnValue(of(sessionsOf(owner)));

      component.confirmRemoveGuest('guest-anna');

      expect(groupSessionsService.getBill$).toHaveBeenCalledOnceWith('group-1');
    });

    it('loads the bill again after a dish is removed on the server', fakeAsync(() => {
      groupSessionsService.removeDish$.and.returnValue(of(sessionsOf(owner)));
      createPage(sessionsOf(owner, anna));
      tick();
      groupSessionsService.getBill$.calls.reset();

      component.removeDish('guest-anna', 'dish-salad');
      tick(REMOVAL_UNDO_DELAY_MS);

      expect(groupSessionsService.getBill$).toHaveBeenCalledOnceWith('group-1');
    }));

    it('keeps the page working when the bill cannot be loaded', async () => {
      groupSessionsService.getBill$.and.returnValue(throwError(() => new Error('boom')));

      await createPage(sessionsOf(owner, anna));

      expect(component.loadState()).toBe('ready');
      expect(component.showBill()).toBeFalse();
    });

    it('copies a readable summary of the bill', async () => {
      groupSessionsService.getBill$.and.returnValue(of(everyone));
      const writeText = jasmine.createSpy('writeText').and.resolveTo();
      spyOnProperty(navigator, 'clipboard', 'get').and.returnValue({ writeText } as unknown as Clipboard);
      await createPage(sessionsOf(owner, anna));

      await component.copyBillSummary();

      expect(writeText).toHaveBeenCalledOnceWith(
        'Table bill\nOwner (you): $10.00\nAnna: $20.00\nBill total: $30.00'
      );
      expect(toastService.success).toHaveBeenCalledWith('Bill summary copied.');
    });
  });

  describe('submitting', () => {
    beforeEach(async () => createPage(sessionsOf(owner, anna)));

    it('submits the pre-order of the reservation and returns to the reservations', () => {
      preOrdersService.submitPreOrderByReservationId.and.returnValue(of({}));

      component.submit();

      expect(preOrdersService.submitPreOrderByReservationId).toHaveBeenCalledOnceWith('res-1');
      expect(toastService.success).toHaveBeenCalledWith('Pre-order submitted.');
      expect(router.navigate).toHaveBeenCalledWith(['/reservations']);
    });

    it('stays on the page and unlocks the button when submitting fails', () => {
      preOrdersService.submitPreOrderByReservationId.and.returnValue(
        throwError(() => new Error('boom'))
      );

      component.submit();

      expect(router.navigate).not.toHaveBeenCalled();
      expect(component.isSubmitting()).toBeFalse();
    });
  });
});
