import { CurrencyPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { map, of, switchMap } from 'rxjs';

import { GroupBill } from '@app/models/group-order/group-bill.model';
import { GroupOrderItem, GroupParticipant, GroupSessions } from '@app/models/group-order/group-sessions.model';
import { GroupSessionsService } from '@app/services/guest-sessions/group-sessions.service';
import { GuestInviteService } from '@app/services/reservation/guest-invite.service';
import { ToastService } from '@app/services/toast/toast.service';
import { HeaderComponent } from '@app/shared/custom-components/header/header.component';

import {
  BannerView,
  BillRowView,
  DishRowView,
  GuestStatusKind,
  GuestView,
  LoadState,
} from './models';

export const REMOVAL_UNDO_DELAY_MS = 5000;

const STATUS_LABELS: Record<GuestStatusKind, string> = {
  confirmed: 'Confirmed',
  choosing: 'Still choosing',
  empty: 'No order yet',
  attention: 'Needs attention',
  changed: 'Changed by you',
};

@Component({
  selector: 'app-group-order-page',
  standalone: true,
  imports: [CurrencyPipe, HeaderComponent],
  templateUrl: './group-order-page.component.html',
  styleUrl: './group-order-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupOrderPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly groupSessionsService = inject(GroupSessionsService);
  private readonly guestInviteService = inject(GuestInviteService);
  private readonly toastService = inject(ToastService);

  private readonly removalTimers = new Map<string, ReturnType<typeof setTimeout>>();

  readonly reservationId = signal('');
  readonly groupReservationId = signal<string | null>(null);
  readonly inviteLink = signal<string | null>(null);
  readonly loadState = signal<LoadState>('loading');
  readonly sessions = signal<GroupSessions | null>(null);
  readonly isSubmitting = signal(false);
  readonly submittedSummary = signal<{ ownerTotal: number; guestsTotal: number; tableTotal: number; guestCount: number } | null>(null);
  readonly bill = signal<GroupBill | null>(null);

  // "sessionId|dishId" of dishes that are hidden from the totals and can still be restored.
  readonly pendingRemovals = signal<readonly string[]>([]);
  // Guests whose dish was already removed on the server during this visit.
  readonly committedChangeIds = signal<readonly string[]>([]);
  readonly confirmingGuestId = signal<string | null>(null);
  private readonly expansionOverrides = signal<Readonly<Record<string, boolean>>>({});

  readonly owner = computed<GroupParticipant | null>(
    () => this.sessions()?.participants.find((participant) => participant.type === 'Owner') ?? null
  );

  readonly ownerRows = computed<DishRowView[]>(() => {
    const owner = this.owner();
    return owner ? owner.items.map((item) => this.toRow(owner.id, item)) : [];
  });

  readonly ownerTotal = computed(() => this.sumRows(this.ownerRows()));
  readonly isOwnerSubmitted = computed(() => this.owner()?.isSubmitted ?? false);

  readonly guestViews = computed<GuestView[]>(() => {
    const participants = this.sessions()?.participants ?? [];
    return participants
      .filter((participant) => participant.type === 'Guest')
      .map((participant) => this.toGuestView(participant));
  });

  readonly submittedGuestsTotal = computed(() =>
    this.guestViews()
      .filter((guest) => guest.isSubmitted)
      .reduce((sum, guest) => sum + guest.total, 0)
  );

  readonly tableTotal = computed(() => this.ownerTotal() + this.submittedGuestsTotal());

  readonly waitingGuests = computed(() => this.guestViews().filter((guest) => !guest.isSubmitted));

  readonly unavailableCount = computed(
    () =>
      this.guestViews()
        .filter((guest) => guest.isSubmitted)
        .flatMap((guest) => guest.rows)
        .filter((row) => row.isUnavailable && !row.isRemoved).length
  );

  readonly billRows = computed<BillRowView[]>(() =>
    (this.bill()?.guests ?? []).map((guest) => ({
      id: guest.guestSessionId ?? guest.name,
      name: guest.name,
      initial: guest.name.trim().charAt(0).toUpperCase() || '?',
      isOwner: guest.isOwner,
      isSubmitted: guest.isSubmitted,
      summary: guest.lines.map((line) => `${line.name} ×${line.quantity}`).join(' · '),
      subtotal: guest.subtotal,
      sharePercent: guest.sharePercent,
    }))
  );

  // There is nothing to split while the owner sits alone or nothing is ordered.
  readonly showBill = computed(() => this.billRows().length > 1 && (this.bill()?.total ?? 0) > 0);

  readonly billTitle = computed(() =>
    this.billRows().some((row) => !row.isSubmitted) ? 'Bill so far' : 'Bill total'
  );

  readonly banner = computed<BannerView | null>(() => {
    if (this.guestViews().length === 0) {
      return { kind: 'info', text: 'Group pre-order · just you so far' };
    }

    const unavailable = this.unavailableCount();
    if (unavailable > 0) {
      const noun = unavailable === 1 ? 'item needs' : 'items need';
      return { kind: 'error', text: `${unavailable} ${noun} your attention before you can submit` };
    }

    const waiting = this.waitingGuests();
    if (waiting.length > 0) {
      const text = waiting
        .map((guest) =>
          guest.hasItems ? `${guest.name} is still choosing` : `${guest.name} has no order yet`
        )
        .join(' · ');
      return { kind: 'warn', text };
    }

    return null;
  });

  readonly submitLabel = computed(() => {
    if (this.isOwnerSubmitted()) {
      return 'Pre-order submitted';
    }
    const waiting = this.waitingGuests().map((guest) => guest.name);
    return waiting.length > 0
      ? `Submit without ${this.joinNames(waiting)}`
      : 'Submit Pre-Order';
  });

  readonly canSubmit = computed(
    () =>
      !this.isSubmitting() &&
      !this.isOwnerSubmitted() &&
      this.pendingRemovals().length === 0 &&
      this.unavailableCount() === 0
  );

  constructor() {
    this.destroyRef.onDestroy(() => this.flushPendingRemovals());
  }

  ngOnInit(): void {
    this.reservationId.set(this.route.snapshot.paramMap.get('reservationId') ?? '');
    this.load();
  }

  load(): void {
    const reservationId = this.reservationId();
    if (!reservationId) {
      this.loadState.set('error');
      return;
    }

    this.loadState.set('loading');
    this.guestInviteService
      .getInviteToken$(reservationId)
      .pipe(
        // The owner must be able to open the page even when the invite itself is "full",
        // so the group id comes with the token; the public invite call is only a fallback.
        switchMap((tokenResponse) => {
          const knownGroupId = tokenResponse.groupReservationId ?? tokenResponse.GroupReservationId;
          return knownGroupId
            ? of({ token: tokenResponse.token, groupId: knownGroupId })
            : this.guestInviteService
                .getInvite$(tokenResponse.token)
                .pipe(map((invite) => ({ token: tokenResponse.token, groupId: invite.GroupReservationId })));
        }),
        switchMap(({ token, groupId }) => {
          this.inviteLink.set(`${window.location.origin}/join/${token}`);
          this.groupReservationId.set(groupId);
          return this.groupSessionsService.getSessions$(groupId);
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (sessions) => {
          this.sessions.set(sessions);
          this.loadState.set('ready');
          this.loadBill();
        },
        error: () => this.loadState.set('error'),
      });
  }

  addDishes(): void {
    void this.router.navigate(['/menu'], {
      queryParams: {
        preOrder: 'true',
        reservationId: this.reservationId(),
        reservationLabel: 'your group order',
      },
    });
  }

  closeSubmittedModal(): void {
    this.submittedSummary.set(null);
    void this.router.navigate(['/reservations']);
  }

  goBack(): void {
    void this.router.navigate(['/reservations']);
  }

  toggleGuest(guest: GuestView): void {
    this.expansionOverrides.update((current) => ({ ...current, [guest.id]: !guest.isExpanded }));
  }

  // ---- removing a dish (with a short undo window) ----

  removeDish(guestId: string, dishId: string): void {
    const key = this.removalKey(guestId, dishId);
    if (this.pendingRemovals().includes(key)) {
      return;
    }

    this.pendingRemovals.update((keys) => [...keys, key]);

    const timer = setTimeout(() => this.commitRemoval(guestId, dishId), REMOVAL_UNDO_DELAY_MS);
    this.removalTimers.set(key, timer);
  }

  undoRemoval(guestId: string, dishId: string): void {
    const key = this.removalKey(guestId, dishId);
    const timer = this.removalTimers.get(key);
    if (timer !== undefined) {
      clearTimeout(timer);
      this.removalTimers.delete(key);
    }

    this.pendingRemovals.update((keys) => keys.filter((existing) => existing !== key));
  }

  // ---- removing a whole guest ----

  askToRemoveGuest(guestId: string): void {
    this.confirmingGuestId.set(guestId);
  }

  cancelRemoveGuest(): void {
    this.confirmingGuestId.set(null);
  }

  confirmRemoveGuest(guestId: string): void {
    const groupId = this.groupReservationId();
    if (!groupId) {
      return;
    }

    this.groupSessionsService
      .removeGuest$(groupId, guestId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (sessions) => {
          this.sessions.set(sessions);
          this.confirmingGuestId.set(null);
          this.toastService.success('Guest removed from the group order.');
          this.loadBill();
        },
        error: () => this.confirmingGuestId.set(null),
      });
  }

  // ---- invite helpers ----

  async copyInviteLink(): Promise<void> {
    const link = this.inviteLink();
    if (!link) {
      this.toastService.error('The invite link is not available yet.');
      return;
    }
    await this.copyToClipboard(link, 'Invite link copied.');
  }

  async copyReminder(guest: GuestView): Promise<void> {
    const link = this.inviteLink();
    if (!link) {
      this.toastService.error('The invite link is not available yet.');
      return;
    }
    await this.copyToClipboard(
      `Hi ${guest.name}! Please choose your dishes for our table: ${link}`,
      'Reminder copied.'
    );
  }

  async copyBillSummary(): Promise<void> {
    const rows = this.billRows();
    const lines = [
      'Table bill',
      ...rows.map((row) => `${row.name}${row.isOwner ? ' (you)' : ''}: ${this.formatMoney(row.subtotal)}`),
      `${this.billTitle()}: ${this.formatMoney(this.bill()?.total ?? 0)}`,
    ];
    await this.copyToClipboard(lines.join('\n'), 'Bill summary copied.');
  }

  // ---- submit ----

  submit(): void {
    if (!this.canSubmit()) {
      return;
    }

    const groupId = this.groupReservationId();
    if (!groupId) {
      return;
    }

    this.isSubmitting.set(true);
    this.groupSessionsService
      .submitGroupPreOrder$(groupId, this.reservationId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.isSubmitting.set(false);
          this.submittedSummary.set({
            ownerTotal: this.ownerTotal(),
            guestsTotal: this.submittedGuestsTotal(),
            tableTotal: this.tableTotal(),
            guestCount: this.guestViews().filter((guest) => guest.isSubmitted).length,
          });
        },
        error: () => {
          this.isSubmitting.set(false);
          this.toastService.error('Could not submit the group pre-order. Please try again.');
        },
      });
  }

  // ---- internals ----

  private loadBill(): void {
    const groupId = this.groupReservationId();
    if (!groupId) {
      return;
    }

    this.groupSessionsService
      .getBill$(groupId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (bill) => this.bill.set(bill),
        error: () => this.bill.set(null),
      });
  }

  private formatMoney(amount: number): string {
    return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(amount);
  }


  private commitRemoval(guestId: string, dishId: string): void {
    const key = this.removalKey(guestId, dishId);
    this.removalTimers.delete(key);

    const groupId = this.groupReservationId();
    if (!groupId) {
      return;
    }

    this.groupSessionsService
      .removeDish$(groupId, guestId, dishId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (sessions) => {
          this.sessions.set(sessions);
          this.committedChangeIds.update((ids) => (ids.includes(guestId) ? ids : [...ids, guestId]));
          this.pendingRemovals.update((keys) => keys.filter((existing) => existing !== key));
          this.loadBill();
        },
        error: () => {
          this.pendingRemovals.update((keys) => keys.filter((existing) => existing !== key));
        },
      });
  }

  // When the owner leaves the page, removals that were not undone must still happen.
  private flushPendingRemovals(): void {
    const groupId = this.groupReservationId();
    for (const [key, timer] of this.removalTimers) {
      clearTimeout(timer);
      if (groupId) {
        const [guestId, dishId] = key.split('|');
        this.groupSessionsService.removeDish$(groupId, guestId, dishId).subscribe();
      }
    }
    this.removalTimers.clear();
  }

  private toGuestView(guest: GroupParticipant): GuestView {
    const rows = guest.items.map((item) => this.toRow(guest.id, item));
    const total = this.sumRows(rows);
    const hasItems = rows.some((row) => !row.isRemoved);
    const hasUnavailable = rows.some((row) => row.isUnavailable && !row.isRemoved);
    const isChanged =
      this.committedChangeIds().includes(guest.id) || rows.some((row) => row.isRemoved);

    const statusKind = this.statusKindFor(guest.isSubmitted, hasItems, hasUnavailable, isChanged);

    return {
      id: guest.id,
      name: guest.name,
      initial: guest.name.trim().charAt(0).toUpperCase() || '?',
      statusKind,
      statusLabel: STATUS_LABELS[statusKind],
      total,
      isSubmitted: guest.isSubmitted,
      hasItems,
      isExpanded: this.expansionOverrides()[guest.id] ?? guest.isSubmitted,
      isConfirmingRemoval: this.confirmingGuestId() === guest.id,
      rows,
    };
  }

  private statusKindFor(
    isSubmitted: boolean,
    hasItems: boolean,
    hasUnavailable: boolean,
    isChanged: boolean
  ): GuestStatusKind {
    if (isSubmitted && hasUnavailable) {
      return 'attention';
    }
    if (isChanged) {
      return 'changed';
    }
    if (isSubmitted) {
      return 'confirmed';
    }
    return hasItems ? 'choosing' : 'empty';
  }

  private toRow(guestId: string, item: GroupOrderItem): DishRowView {
    return {
      dishId: item.dish.id,
      name: item.dish.name,
      imageUrl: item.dish.imageUrl,
      quantity: item.quantity,
      lineTotal: item.dish.price * item.quantity,
      isUnavailable: !item.dish.isAvailable,
      isRemoved: this.pendingRemovals().includes(this.removalKey(guestId, item.dish.id)),
    };
  }

  private sumRows(rows: DishRowView[]): number {
    return rows.filter((row) => !row.isRemoved).reduce((sum, row) => sum + row.lineTotal, 0);
  }

  private removalKey(guestId: string, dishId: string): string {
    return `${guestId}|${dishId}`;
  }

  private joinNames(names: string[]): string {
    if (names.length <= 1) {
      return names.join('');
    }
    return `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`;
  }

  private async copyToClipboard(text: string, successMessage: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      this.toastService.success(successMessage);
    } catch {
      this.toastService.error('Could not copy. Please copy it manually.');
    }
  }
}
