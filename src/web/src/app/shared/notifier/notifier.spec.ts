import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Notifier } from './notifier';

describe('Notifier', () => {
  let open: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    open = vi.fn();
    TestBed.configureTestingModule({ providers: [{ provide: MatSnackBar, useValue: { open } }] });
  });

  it('opens a polite, dismissible snack bar for success', () => {
    TestBed.inject(Notifier).success('Your email is confirmed.');
    expect(open).toHaveBeenCalledWith(
      'Your email is confirmed.',
      'Dismiss',
      expect.objectContaining({ politeness: 'polite', duration: 5000 }),
    );
  });

  it('uses assertive politeness and a longer duration for errors', () => {
    TestBed.inject(Notifier).error('Something went wrong.');
    expect(open).toHaveBeenCalledWith(
      'Something went wrong.',
      'Dismiss',
      expect.objectContaining({ politeness: 'assertive', duration: 8000 }),
    );
  });
});
