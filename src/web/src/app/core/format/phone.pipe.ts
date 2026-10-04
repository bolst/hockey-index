import { Pipe, PipeTransform } from '@angular/core';
import { formatPhone } from './phone';

@Pipe({ name: 'phone' })
export class PhonePipe implements PipeTransform {
  transform(e164: string | null | undefined): string {
    return formatPhone(e164);
  }
}
