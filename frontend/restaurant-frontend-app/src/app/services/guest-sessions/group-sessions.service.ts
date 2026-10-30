import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';

import { GroupBill } from '@app/models/group-order/group-bill.model';
import { GroupSessionsApiResponse } from '@app/models/group-order/group-sessions-api.model';
import { GroupSessions } from '@app/models/group-order/group-sessions.model';
import { mapGroupSessions } from '@app/shared/mappers/group-sessions.mapper';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';

/**
 * Owner-only calls: they live under /guest/groups/... but are authorised with the owner's
 * Cognito token (see token.interceptor.ts), not with a guest session token.
 */
@Injectable({ providedIn: 'root' })
export class GroupSessionsService {
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly http = inject(HttpClient);

  getSessions$(groupReservationId: string): Observable<GroupSessions> {
    return this.http
      .get<GroupSessionsApiResponse>(this.sessionsUrl(groupReservationId))
      .pipe(map(mapGroupSessions));
  }

  getBill$(groupReservationId: string): Observable<GroupBill> {
    return this.http.get<GroupBill>(
      `${this.baseUrl}/guest/groups/${encodeURIComponent(groupReservationId)}/bill`
    );
  }

  removeGuest$(groupReservationId: string, guestSessionId: string): Observable<GroupSessions> {
    return this.http
      .delete<GroupSessionsApiResponse>(
        `${this.sessionsUrl(groupReservationId)}/${encodeURIComponent(guestSessionId)}`
      )
      .pipe(map(mapGroupSessions));
  }

  removeDish$(
    groupReservationId: string,
    guestSessionId: string,
    dishId: string
  ): Observable<GroupSessions> {
    return this.http
      .delete<GroupSessionsApiResponse>(
        `${this.sessionsUrl(groupReservationId)}/${encodeURIComponent(guestSessionId)}/dishes/${encodeURIComponent(dishId)}`
      )
      .pipe(map(mapGroupSessions));
  }

  /**
   * Owner-only: consolidates every submitted guest session's dishes (by dishId) into the
   * reservation's real Order, moves the Order to SubmittedByCustomer, and closes the group
   * (Status -> Submitted). Returns the merged dish list the backend added to the order.
   */
  submitGroupPreOrder$(
    groupReservationId: string,
    reservationId: string
  ): Observable<{ dishId: string; quantity: number }[]> {
    return this.http.post<{ dishId: string; quantity: number }[]>(
      `${this.baseUrl}/group-reservations/${encodeURIComponent(groupReservationId)}/submit`,
      { reservationId }
    );
  }

  private sessionsUrl(groupReservationId: string): string {
    return `${this.baseUrl}/guest/groups/${encodeURIComponent(groupReservationId)}/sessions`;
  }
}
