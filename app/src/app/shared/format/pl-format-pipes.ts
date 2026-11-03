import { Pipe, PipeTransform } from '@angular/core';
import { AmountInput, formatDate, formatDateTime, formatMonth, formatPln } from './pl-format';

@Pipe({ name: 'plnAmount' })
export class PlnAmountPipe implements PipeTransform {
  transform(value: AmountInput): string {
    return formatPln(value);
  }
}

@Pipe({ name: 'plDate' })
export class PlDatePipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return formatDate(value);
  }
}

@Pipe({ name: 'plMonth' })
export class PlMonthPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return formatMonth(value);
  }
}

@Pipe({ name: 'plDateTime' })
export class PlDateTimePipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return formatDateTime(value);
  }
}
