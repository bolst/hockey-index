import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatAutocomplete, MatAutocompleteSelectedEvent, MatAutocompleteTrigger } from '@angular/material/autocomplete';
import { MatIconButton } from '@angular/material/button';
import { MatFormField, MatLabel, MatPrefix, MatSuffix } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { MatOption } from '@angular/material/select';
import { MatTooltip } from '@angular/material/tooltip';
import { toSignal } from '@angular/core/rxjs-interop';
import { City, cityLabel, searchCities } from '../../core/geo/cities';

/** City search for the search location, plus a button to use the device position again. */
@Component({
  selector: 'app-location-field',
  imports: [
    ReactiveFormsModule,
    MatFormField,
    MatLabel,
    MatPrefix,
    MatSuffix,
    MatInput,
    MatIcon,
    MatIconButton,
    MatAutocomplete,
    MatAutocompleteTrigger,
    MatOption,
    MatTooltip,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <mat-form-field appearance="outline" subscriptSizing="dynamic" floatLabel="always" [class.showing-place]="!!label() && !focused()">
      <mat-label>Location</mat-label>
      <mat-icon matPrefix aria-hidden="true">location_on</mat-icon>
      <input
        matInput
        type="search"
        autocomplete="off"
        [placeholder]="placeholder()"
        [formControl]="query"
        [matAutocomplete]="cities"
        (focus)="focused.set(true)"
        (blur)="focused.set(false)"
      />
      <button
        matIconButton
        matSuffix
        type="button"
        aria-label="Use my location"
        matTooltip="Use my location"
        [disabled]="locating()"
        (click)="useDevice.emit()"
      >
        <mat-icon aria-hidden="true">my_location</mat-icon>
      </button>
      <mat-autocomplete #cities="matAutocomplete" (optionSelected)="choose($event)">
        @for (city of matches(); track cityLabel(city)) {
          <mat-option [value]="city">{{ cityLabel(city) }}</mat-option>
        }
      </mat-autocomplete>
    </mat-form-field>
  `,
  styles: `
    :host {
      display: block;
    }

    mat-form-field {
      width: 100%;
    }

    input.mat-mdc-input-element {
      text-overflow: ellipsis;
    }

    .showing-place input.mat-mdc-input-element::placeholder {
      color: var(--mat-sys-on-surface);
      opacity: 1;
    }
  `,
})
export class LocationField {
  /** Current location description, shown while the field is empty. */
  readonly label = input<string>('');
  readonly locating = input(false);
  readonly citySelected = output<City>();
  readonly useDevice = output<void>();

  protected readonly cityLabel = cityLabel;
  protected readonly query = new FormControl<string | City>('', { nonNullable: true });
  protected readonly focused = signal(false);
  private readonly queryValue = toSignal(this.query.valueChanges, { initialValue: '' });
  protected readonly matches = computed(() => {
    const value = this.queryValue();
    return searchCities(typeof value === 'string' ? value : '');
  });
  protected readonly placeholder = computed(() => (this.focused() ? 'Search a city' : this.label() || 'Search a city'));

  protected choose(event: MatAutocompleteSelectedEvent): void {
    const city = event.option.value as City;
    this.query.setValue('');
    this.citySelected.emit(city);
  }
}
