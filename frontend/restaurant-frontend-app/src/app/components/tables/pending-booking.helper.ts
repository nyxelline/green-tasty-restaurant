import { TableFilter } from '@app/models/tables/table-filter.model';
import { TimeRange } from '@app/models/tables/time-range.model';

const KEY = 'green-tasty.pending-booking';

export interface PendingBooking {
  filter: TableFilter;
  tableId: string;
  timeRange: TimeRange;
  guests: number;
}

export function savePendingBooking(value: PendingBooking): void {
  try {
    sessionStorage.setItem(KEY, JSON.stringify(value));
  } catch {
    // storage unavailable: booking just won't be restored
  }
}

export function hasPendingBooking(): boolean {
  try {
    return sessionStorage.getItem(KEY) !== null;
  } catch {
    return false;
  }
}

export function takePendingBooking(): PendingBooking | null {
  try {
    const raw = sessionStorage.getItem(KEY);
    if (!raw) {
      return null;
    }
    sessionStorage.removeItem(KEY);
    return JSON.parse(raw) as PendingBooking;
  } catch {
    return null;
  }
}
