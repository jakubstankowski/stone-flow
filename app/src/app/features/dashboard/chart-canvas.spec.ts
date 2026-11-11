import { TestBed } from '@angular/core/testing';
import { formatPln } from '../../shared/format/pl-format';
import { ChartCanvas, ChartSpec, chartConfig, chartNumber } from './chart-canvas';
import { ActivePoint, CHART_FACTORY, ChartConfig, ChartHandle } from './chart-factory';

class FakeChart implements ChartHandle {
  readonly active: ActivePoint[][] = [];
  readonly tooltipActive: ActivePoint[][] = [];
  updates = 0;
  destroyed = false;
  data: ChartConfig['data'];
  readonly tooltip = {
    setActiveElements: (points: ActivePoint[]) => this.tooltipActive.push(points),
  };

  constructor(
    readonly canvas: HTMLCanvasElement,
    readonly config: ChartConfig,
  ) {
    this.data = config.data;
  }

  update(): void {
    this.updates++;
  }

  destroy(): void {
    this.destroyed = true;
  }

  setActiveElements(points: ActivePoint[]): void {
    this.active.push(points);
  }
}

const TIMELINE: ChartSpec = {
  type: 'timeline',
  labels: ['01.11', '02.11', '03.11'],
  series: [
    {
      label: 'Sprzedaż',
      values: ['1000.00', '-300.00', '0.00'],
      token: '--sf-accent',
      shape: 'bar',
    },
    {
      label: 'Wynik przed wypłatami',
      values: ['700.00', '-300.00', '0.00'],
      token: '--sf-info',
      shape: 'dashedLine',
    },
  ],
  ariaLabel: 'Sprzedaż i koszty w czasie. Dane w tabeli poniżej.',
};

const CHANNELS: ChartSpec = {
  type: 'horizontalBar',
  labels: ['Sklep', 'OLX'],
  series: [{ label: 'Sprzedaż', values: ['0.00', '800.00'], token: '--sf-accent', shape: 'bar' }],
  ariaLabel: 'Sprzedaż według kanałów. Wartości na liście obok.',
};

describe('ChartCanvas', () => {
  let charts: FakeChart[];

  beforeEach(() => {
    charts = [];
    TestBed.configureTestingModule({
      imports: [ChartCanvas],
      providers: [
        {
          provide: CHART_FACTORY,
          useValue: (canvas: HTMLCanvasElement, config: ChartConfig) => {
            const chart = new FakeChart(canvas, config);
            charts.push(chart);
            return chart;
          },
        },
      ],
    });
  });

  async function render(spec: ChartSpec) {
    const fixture = TestBed.createComponent(ChartCanvas);
    fixture.componentRef.setInput('spec', spec);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const canvas = element.querySelector('canvas') as HTMLCanvasElement;
    const live = element.querySelector('[aria-live="polite"]') as HTMLElement;
    const press = async (key: string) => {
      canvas.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
      await fixture.whenStable();
    };
    return { fixture, canvas, live, press };
  }

  it('renders an accessible canvas once and updates the same chart for a new spec', async () => {
    const { fixture, canvas } = await render(TIMELINE);

    expect(canvas.getAttribute('role')).toBe('img');
    expect(canvas.getAttribute('tabindex')).toBe('0');
    expect(canvas.getAttribute('aria-label')).toBe(TIMELINE.ariaLabel);
    expect(charts).toHaveLength(1);
    expect(charts[0].canvas).toBe(canvas);

    fixture.componentRef.setInput('spec', { ...TIMELINE, labels: ['04.11', '05.11', '06.11'] });
    await fixture.whenStable();
    expect(charts).toHaveLength(1);
    expect(charts[0].data.labels).toEqual(['04.11', '05.11', '06.11']);
    expect(charts[0].updates).toBe(1);
  });

  it('moves the active point with the arrow keys, Home and End and announces the API strings', async () => {
    const { live, press } = await render(TIMELINE);
    const chart = charts[0];

    await press('ArrowRight');
    expect(chart.active.at(-1)).toEqual([
      { datasetIndex: 0, index: 0 },
      { datasetIndex: 1, index: 0 },
    ]);
    expect(chart.tooltipActive.at(-1)).toEqual(chart.active.at(-1));
    expect(live.textContent?.trim()).toBe(
      `01.11: Sprzedaż ${formatPln('1000.00')}, Wynik przed wypłatami ${formatPln('700.00')}`,
    );

    await press('ArrowRight');
    expect(live.textContent).toContain(`Sprzedaż ${formatPln('-300.00')}`);

    await press('End');
    expect(chart.active.at(-1)?.[0].index).toBe(2);
    await press('ArrowRight');
    expect(chart.active.at(-1)?.[0].index).toBe(2);
    await press('Home');
    expect(chart.active.at(-1)?.[0].index).toBe(0);
    await press('ArrowLeft');
    expect(chart.active.at(-1)?.[0].index).toBe(0);

    await press('Escape');
    expect(chart.active.at(-1)).toEqual([]);
    expect(chart.tooltipActive.at(-1)).toEqual([]);
    expect(live.textContent?.trim()).toBe('');
  });

  it('uses up and down arrows for horizontal bars', async () => {
    const { live, press } = await render(CHANNELS);
    const chart = charts[0];

    await press('ArrowRight');
    expect(chart.active).toHaveLength(0);
    await press('ArrowDown');
    await press('ArrowDown');
    expect(chart.active.at(-1)).toEqual([{ datasetIndex: 0, index: 1 }]);
    expect(live.textContent?.trim()).toBe(`OLX: Sprzedaż ${formatPln('800.00')}`);
    await press('ArrowUp');
    expect(chart.active.at(-1)).toEqual([{ datasetIndex: 0, index: 0 }]);
  });

  it('destroys the chart with the component', async () => {
    const { fixture } = await render(TIMELINE);

    fixture.destroy();

    expect(charts[0].destroyed).toBe(true);
  });

  it('takes colours from tokens, draws the result as a dashed line with markers and starts at zero', () => {
    const tokens: string[] = [];
    const config = chartConfig(
      TIMELINE,
      (token) => {
        tokens.push(token);
        return `color(${token})`;
      },
      true,
    );
    const [bar, line] = config.data.datasets as unknown as Record<string, unknown>[];
    const options = config.options as unknown as Record<
      string,
      Record<string, Record<string, unknown>>
    >;

    expect(tokens).toEqual(['--sf-accent', '--sf-accent', '--sf-info', '--sf-info']);
    expect(bar['type']).toBe('bar');
    expect(bar['backgroundColor']).toBe('color(--sf-accent)');
    expect(bar['data']).toEqual([1000, -300, 0]);
    expect(line['type']).toBe('line');
    expect(line['borderDash']).toEqual([6, 4]);
    expect(line['pointRadius']).toBeGreaterThan(0);
    expect(options['scales']['y']['beginAtZero']).toBe(true);
    expect(options['scales']['y']['title']).toEqual({ display: true, text: 'zł' });
    expect(config.options?.animation).toBe(false);
    expect(chartConfig(CHANNELS, () => '', false).options?.indexAxis).toBe('y');
    expect(chartConfig(TIMELINE, () => '', false).options?.animation).toBeUndefined();
  });

  it('formats the tooltip with the API string and converts text to numbers only for geometry', () => {
    const config = chartConfig(TIMELINE, () => '', false);
    const label = config.options?.plugins?.tooltip?.callbacks?.label as unknown as (context: {
      dataset: { label: string };
      datasetIndex: number;
      dataIndex: number;
    }) => string;

    expect(label({ dataset: { label: 'Sprzedaż' }, datasetIndex: 0, dataIndex: 1 })).toBe(
      `Sprzedaż: ${formatPln('-300.00')}`,
    );
    expect(chartNumber('1234.50')).toBe(1234.5);
  });
});
