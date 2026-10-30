import {
  Component,
  EventEmitter,
  Output,
  ChangeDetectionStrategy,
  computed,
  input,
} from '@angular/core';
import { TimeSlotComponent } from '../time-slot/time-slot.component';
import { Table } from '@app/models/tables/table.model';
import { TimeRange } from '@app/models/tables/time-range.model';
import { HourSlot } from './models';
import { formatTime } from '@app/shared/helpers/date-helpers';

const FIRST_SLOT_MINUTES = 10 * 60;
const LAST_SLOT_MINUTES = 21 * 60 + 30;
const SLOT_STEP_MINUTES = 30;
// How far around the guest's picked time to show slots for, instead of dumping the whole day's
// grid on them — the picked time stays the obvious default while still letting them pick a
// nearby slot without scrolling through hours they didn't ask for.
const WINDOW_AROUND_CENTER_MINUTES = 2 * 60;

@Component({
  selector: 'app-table-card',
  imports: [TimeSlotComponent],
  templateUrl: './table-card.component.html',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './table-card.component.scss',
})
export class TableCardComponent {
  readonly location = input.required<string>();
  readonly date = input.required<string>();
  readonly table = input.required<Table>();
  // "HH:mm" of the time the guest picked in the search form, if any.
  readonly centerTime = input<string | null>(null);
  // @Output() showAllClicked = new EventEmitter<Table>();
  @Output() timeSlotSelected = new EventEmitter<TimeRange>();

  protected readonly hourSlots = computed<HourSlot[]>(() => this.buildHourSlots());

  onTimeSlotSelected(slot: HourSlot): void {
    if (slot.timeRange) {
      this.timeSlotSelected.emit(slot.timeRange);
    }
  }

  // onShowAllClick(): void {
  //   this.showAllClicked.emit(this.table);
  // }

  private buildHourSlots(): HourSlot[] {
    const timeRanges = this.table().timeRanges ?? [];
    const slots: HourSlot[] = [];
    const [rangeStart, rangeEnd] = this.resolveSlotRange();

    for (
      let slotStart = rangeStart;
      slotStart <= rangeEnd;
      slotStart += SLOT_STEP_MINUTES
    ) {
      const timeRange = timeRanges.find((range) => {
        const start = new Date(range.start);
        const minutes = start.getHours() * 60 + start.getMinutes();
        return minutes >= slotStart && minutes < slotStart + SLOT_STEP_MINUTES;
      });
      const hour = Math.floor(slotStart / 60);
      const minute = (slotStart % 60).toString().padStart(2, '0');
      const bucketLabel = `${hour}:${minute}`;
      slots.push({
        label: timeRange ? formatTime(timeRange.start) : bucketLabel,
        timeRange: timeRange ?? null,
      });
    }

    return slots;
  }

  private resolveSlotRange(): [number, number] {
    const centerMinutes = this.parseCenterMinutes();
    if (centerMinutes === null) {
      return [FIRST_SLOT_MINUTES, LAST_SLOT_MINUTES];
    }

    const start = Math.max(FIRST_SLOT_MINUTES, centerMinutes - WINDOW_AROUND_CENTER_MINUTES);
    const end = Math.min(LAST_SLOT_MINUTES, centerMinutes + WINDOW_AROUND_CENTER_MINUTES);
    return [start, end];
  }

  private parseCenterMinutes(): number | null {
    const time = this.centerTime();
    if (!time) {
      return null;
    }

    const [hoursStr, minutesStr] = time.split(':');
    const hours = Number(hoursStr);
    const minutes = Number(minutesStr);
    if (Number.isNaN(hours) || Number.isNaN(minutes)) {
      return null;
    }

    return hours * 60 + minutes;
  }
}
