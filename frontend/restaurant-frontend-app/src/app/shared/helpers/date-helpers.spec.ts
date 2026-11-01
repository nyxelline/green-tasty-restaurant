import { addMinutesIso, findClosestDuration, minutesBetween, revertFormatTime } from './date-helpers';

describe('date-helpers', () => {
  describe('addMinutesIso', () => {
    const cases: { name: string; input: string; minutes: number; expected: string }[] = [
      {
        name: 'adds minutes within the same hour',
        input: '2026-10-17T12:00:00.000Z',
        minutes: 30,
        expected: '2026-10-17T12:30:00.000Z',
      },
      {
        name: 'adds several hours',
        input: '2026-10-17T12:00:00.000Z',
        minutes: 180,
        expected: '2026-10-17T15:00:00.000Z',
      },
      {
        name: 'crosses midnight into the next day',
        input: '2026-10-17T23:30:00.000Z',
        minutes: 90,
        expected: '2026-10-18T01:00:00.000Z',
      },
      {
        name: 'returns the same instant for zero minutes',
        input: '2026-10-17T12:00:00.000Z',
        minutes: 0,
        expected: '2026-10-17T12:00:00.000Z',
      },
      {
        name: 'accepts ISO strings without milliseconds',
        input: '2026-10-17T12:00:00Z',
        minutes: 120,
        expected: '2026-10-17T14:00:00.000Z',
      },
    ];

    cases.forEach(({ name, input, minutes, expected }) => {
      it(name, () => {
        expect(addMinutesIso(input, minutes)).toBe(expected);
      });
    });
  });

  describe('minutesBetween', () => {
    it('returns the length of a 90-minute slot', () => {
      expect(minutesBetween('2026-10-17T12:00:00Z', '2026-10-17T13:30:00Z')).toBe(90);
    });

    it('counts across midnight', () => {
      expect(minutesBetween('2026-10-17T23:00:00Z', '2026-10-18T01:00:00Z')).toBe(120);
    });

    it('returns zero for the same instant', () => {
      expect(minutesBetween('2026-10-17T12:00:00Z', '2026-10-17T12:00:00Z')).toBe(0);
    });
  });

  describe('revertFormatTime', () => {
    afterEach(() => {
      jasmine.clock().uninstall();
    });

    it('does not roll over into the next month when "today" is a day that does not exist in the target month (regression)', () => {
      // Reproduces: editing a reservation to Nov 3 while "today" is Oct 31 silently saved it
      // as Dec 3. The old implementation built the result via sequential setFullYear/setMonth/
      // setDate calls on `new Date()` (today) - setting the month to November while the day
      // was still 31 overflowed past November's 30 days into December *before* setDate(3) ran.
      jasmine.clock().install();
      jasmine.clock().mockDate(new Date(2026, 9, 31, 12, 0, 0)); // Oct 31, 2026, local time

      const result = new Date(revertFormatTime('11:00', '2026-11-03T12:00:00.000Z'));

      expect(result.getMonth()).toBe(10); // November (0-indexed)
      expect(result.getDate()).toBe(3);
      expect(result.getHours()).toBe(11);
      expect(result.getMinutes()).toBe(0);
    });

    it('converts a 24-hour time string onto the given date', () => {
      const result = new Date(revertFormatTime('14:30', '2026-06-15T12:00:00.000Z'));

      expect(result.getFullYear()).toBe(2026);
      expect(result.getMonth()).toBe(5); // June
      expect(result.getDate()).toBe(15);
      expect(result.getHours()).toBe(14);
      expect(result.getMinutes()).toBe(30);
    });
  });

  describe('findClosestDuration', () => {
    const durations = [90, 120, 150, 180];
    const cases: { name: string; minutes: number; expected: number }[] = [
      { name: 'returns the exact match', minutes: 120, expected: 120 },
      { name: 'rounds down to the nearest option', minutes: 130, expected: 120 },
      { name: 'rounds up to the nearest option', minutes: 145, expected: 150 },
      { name: 'picks the shorter option on a tie', minutes: 135, expected: 120 },
      { name: 'clamps below the shortest option', minutes: 30, expected: 90 },
      { name: 'clamps above the longest option', minutes: 600, expected: 180 },
      { name: 'handles zero minutes', minutes: 0, expected: 90 },
    ];

    cases.forEach(({ name, minutes, expected }) => {
      it(name, () => {
        expect(findClosestDuration(minutes, durations)).toBe(expected);
      });
    });

    it('returns the only option when there is one', () => {
      expect(findClosestDuration(500, [60])).toBe(60);
    });
  });
});
