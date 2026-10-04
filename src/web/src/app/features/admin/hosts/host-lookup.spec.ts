import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { buttonIn, fieldIn, typeInto } from '../admin-testing';
import { HostLookup } from './host-lookup';

describe('HostLookup', () => {
  it('navigates to the host view for a valid GUID and rejects others', () => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(HostLookup);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    typeInto(fieldIn(element, 'Host ID'), 'nope');
    buttonIn(element, 'search Look up').click();
    fixture.detectChanges();
    expect(navigate).not.toHaveBeenCalled();
    expect(element.textContent).toContain('Enter a host ID');

    typeInto(fieldIn(element, 'Host ID'), ' 3F2504E0-4F89-11D3-9A0C-0305E82C3301 ');
    buttonIn(element, 'search Look up').click();
    expect(navigate).toHaveBeenCalledWith(['/admin/hosts', '3f2504e0-4f89-11d3-9a0c-0305e82c3301']);
  });
});
