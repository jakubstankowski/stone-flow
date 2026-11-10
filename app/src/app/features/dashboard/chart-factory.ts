import { InjectionToken } from '@angular/core';
import {
  BarController,
  BarElement,
  CategoryScale,
  Chart,
  type ChartConfiguration,
  Legend,
  LineController,
  LineElement,
  LinearScale,
  PointElement,
  Tooltip,
} from 'chart.js';

/**
 * Jedyny import `chart.js` w aplikacji (SF-19). Rejestrowane są tylko używane kontrolery,
 * elementy, skale i wtyczki (tree-shaking), a biblioteka trafia wyłącznie do fragmentu Pulpitu.
 */
Chart.register(
  BarController,
  BarElement,
  LineController,
  LineElement,
  PointElement,
  CategoryScale,
  LinearScale,
  Tooltip,
  Legend,
);

export type ChartConfig = ChartConfiguration<'bar' | 'line', number[], string>;

export interface ActivePoint {
  readonly datasetIndex: number;
  readonly index: number;
}

/** Część API wykresu używana przez adapter (zastępowana w testach w jsdom, bez canvas). */
export interface ChartHandle {
  data: ChartConfig['data'];
  update(): void;
  destroy(): void;
  setActiveElements(active: ActivePoint[]): void;
  readonly tooltip?: {
    setActiveElements(active: ActivePoint[], position: { x: number; y: number }): void;
  };
}

export type ChartFactory = (canvas: HTMLCanvasElement, config: ChartConfig) => ChartHandle;

export const CHART_FACTORY = new InjectionToken<ChartFactory>('CHART_FACTORY', {
  providedIn: 'root',
  factory: () => (canvas, config) => new Chart(canvas, config) as unknown as ChartHandle,
});
