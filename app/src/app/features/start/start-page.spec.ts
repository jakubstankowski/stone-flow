import { TestBed } from '@angular/core/testing';
import { StartPage } from './start-page';

describe('StartPage', () => {
  it('renders the product name as the heading', async () => {
    await TestBed.configureTestingModule({ imports: [StartPage] }).compileComponents();
    const fixture = TestBed.createComponent(StartPage);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('h1')?.textContent?.trim()).toBe('Stone Flow');
  });
});
