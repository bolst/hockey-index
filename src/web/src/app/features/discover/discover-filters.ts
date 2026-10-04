import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatChipListbox, MatChipListboxChange, MatChipOption } from '@angular/material/chips';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatOption, MatSelect } from '@angular/material/select';
import { MatSlider, MatSliderRangeThumb } from '@angular/material/slider';
import { EventType } from '../../core/api/discovery-api';
import { SkillLevelsLink } from '../../shared/skill-levels-link/skill-levels-link';
import { skillLabel } from '../../../shared-render/event-summary';
import {
  DAYS_OPTIONS,
  Days,
  EVENT_TYPES,
  MAX_FEE_OPTIONS,
  MaxFee,
  RADIUS_OPTIONS,
  Radius,
  SKILL_MAX,
  SKILL_MIN,
  SearchFilters,
  daysLabel,
  maxFeeLabel,
  typeLabel,
} from './search-filters';

/** Search filter controls. Stateless: renders `filters` and emits a full new value on every change. */
@Component({
  selector: 'app-discover-filters',
  imports: [MatFormField, MatLabel, MatSelect, MatOption, MatChipListbox, MatChipOption, MatSlider, MatSliderRangeThumb, SkillLevelsLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.stacked]': 'stacked()' },
  template: `
    <div class="selects">
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>Distance</mat-label>
        <mat-select [value]="filters().radius" (valueChange)="patch({ radius: $event })">
          @for (radius of radiusOptions; track radius) {
            <mat-option [value]="radius">Within {{ radius }} mi</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>When</mat-label>
        <mat-select [value]="filters().days" (valueChange)="patch({ days: $event })">
          @for (days of daysOptions; track days) {
            <mat-option [value]="days">{{ daysLabel(days) }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>Max fee</mat-label>
        <mat-select [value]="filters().maxFee" (valueChange)="patch({ maxFee: $event })">
          <mat-option [value]="null">{{ maxFeeLabel(null) }}</mat-option>
          @for (fee of feeOptions; track fee) {
            <mat-option [value]="fee">{{ maxFeeLabel(fee) }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
    </div>

    <div class="row">
      <mat-chip-listbox aria-label="Event type" [value]="filters().type ?? 'any'" (change)="typeChanged($event)">
        <mat-chip-option value="any" [selectable]="filters().type !== null">All types</mat-chip-option>
        @for (type of types; track type) {
          <mat-chip-option [value]="type" [selectable]="filters().type !== type">{{ typeLabel(type) }}</mat-chip-option>
        }
      </mat-chip-listbox>

      <div class="skill">
        <div class="skill-heading">
          <span class="skill-label" aria-live="polite">{{ skillText() }}</span>
          <app-skill-levels-link />
        </div>
        <mat-slider [min]="skillMin" [max]="skillMax" [step]="1" [discrete]="true" [showTickMarks]="true">
          <input
            matSliderStartThumb
            aria-label="Lowest skill level"
            [value]="filters().skillMin"
            (valueChange)="patch({ skillMin: $event })"
          />
          <input
            matSliderEndThumb
            aria-label="Highest skill level"
            [value]="filters().skillMax"
            (valueChange)="patch({ skillMax: $event })"
          />
        </mat-slider>
      </div>
    </div>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .selects {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 12px;
    }

    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 8px 24px;
    }

    .skill {
      display: flex;
      align-items: center;
      gap: 12px;
      flex: 0 1 340px;
      min-width: 240px;
    }

    .skill-heading {
      display: flex;
      flex-direction: column;
      flex: none;
    }

    :host(.stacked) .skill-heading {
      flex-direction: row;
      flex-wrap: wrap;
      align-items: baseline;
      justify-content: space-between;
      gap: 4px 16px;
    }

    .skill-label {
      flex: none;
      min-width: 9ch;
      font: var(--mat-sys-label-large);
      color: var(--mat-sys-on-surface-variant);
    }

    mat-slider {
      flex: 1 1 auto;
      min-width: 0;
    }

    :host(.stacked) {
      gap: 20px;
    }

    :host(.stacked) .selects {
      grid-template-columns: 1fr;
    }

    :host(.stacked) .row {
      flex-direction: column;
      align-items: stretch;
    }

    :host(.stacked) .skill {
      flex-basis: auto;
      flex-direction: column;
      align-items: stretch;
      gap: 0;
    }
  `,
})
export class DiscoverFilters {
  readonly filters = input.required<SearchFilters>();
  /** Single-column layout for the mobile bottom sheet. */
  readonly stacked = input(false);
  readonly filtersChange = output<SearchFilters>();

  protected readonly radiusOptions: readonly Radius[] = RADIUS_OPTIONS;
  protected readonly daysOptions: readonly Days[] = DAYS_OPTIONS;
  protected readonly feeOptions: readonly MaxFee[] = MAX_FEE_OPTIONS;
  protected readonly types = EVENT_TYPES;
  protected readonly skillMin = SKILL_MIN;
  protected readonly skillMax = SKILL_MAX;
  protected readonly daysLabel = daysLabel;
  protected readonly maxFeeLabel = maxFeeLabel;
  protected readonly typeLabel = typeLabel;

  protected readonly skillText = computed(() => {
    const { skillMin, skillMax } = this.filters();
    return skillMin === SKILL_MIN && skillMax === SKILL_MAX ? 'Any skill' : skillLabel({ min: skillMin, max: skillMax });
  });

  protected patch(change: Partial<SearchFilters>): void {
    this.filtersChange.emit({ ...this.filters(), ...change });
  }

  protected typeChanged(change: MatChipListboxChange): void {
    const value = change.value as EventType | 'any' | undefined;
    this.patch({ type: !value || value === 'any' ? null : value });
  }
}
