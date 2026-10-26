// Split bill of a group pre-order (GET /guest/groups/{groupReservationId}/bill).
// Everybody pays for what he or she ordered. The API already sends camelCase.

export interface BillLine {
  dishId: string;
  name: string;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
}

export interface GuestBill {
  guestSessionId: string | null;
  name: string;
  isOwner: boolean;
  isSubmitted: boolean;
  lines: BillLine[];
  subtotal: number;
  /** Share of the whole bill in percent, one decimal place. */
  sharePercent: number;
}

export interface GroupBill {
  groupReservationId: string;
  guests: GuestBill[];
  total: number;
}
