import { NO_DATA } from './pl-format';
import { PlDatePipe, PlDateTimePipe, PlMonthPipe, PlnAmountPipe } from './pl-format-pipes';

describe('Polish format pipes', () => {
  it('delegate to the format functions', () => {
    expect(new PlnAmountPipe().transform(0)).toBe('0,00 zł');
    expect(new PlDatePipe().transform('2026-10-28')).toBe('28.10.2026');
    expect(new PlMonthPipe().transform('2026-10')).toBe('październik 2026');
    expect(new PlDateTimePipe().transform('2026-10-28T13:05:00Z')).toBe('28.10.2026, 14:05');
  });

  it('show missing data for null', () => {
    expect(new PlnAmountPipe().transform(null)).toBe(NO_DATA);
    expect(new PlDatePipe().transform(null)).toBe(NO_DATA);
  });
});
