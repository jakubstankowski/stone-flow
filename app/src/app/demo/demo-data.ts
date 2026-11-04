/**
 * TRYB DEMO: fikcyjne dane sklepu z kamieniem naturalnym do zapowiedzi kursu. Nazwy klientów,
 * numery i kwoty są wymyślone. W docelowej aplikacji te dane i obliczenia pochodzą z API.
 */

export interface MonthSummary {
  /** `yyyy-MM` */
  readonly month: string;
  readonly sales: number;
  readonly inflows: number;
  readonly expenses: number;
}

export const MONTHS: readonly MonthSummary[] = [
  { month: '2025-11', sales: 61_240, inflows: 55_870, expenses: 38_120 },
  { month: '2025-12', sales: 48_930, inflows: 52_400, expenses: 41_760 },
  { month: '2026-01', sales: 39_580, inflows: 36_210, expenses: 33_950 },
  { month: '2026-02', sales: 44_720, inflows: 41_330, expenses: 35_480 },
  { month: '2026-03', sales: 68_150, inflows: 59_940, expenses: 44_210 },
  { month: '2026-04', sales: 82_460, inflows: 74_120, expenses: 51_380 },
  { month: '2026-05', sales: 95_310, inflows: 88_760, expenses: 58_940 },
  { month: '2026-06', sales: 104_880, inflows: 97_350, expenses: 61_270 },
  { month: '2026-07', sales: 99_640, inflows: 101_230, expenses: 59_830 },
  { month: '2026-08', sales: 91_270, inflows: 93_480, expenses: 56_610 },
  { month: '2026-09', sales: 87_930, inflows: 84_050, expenses: 54_190 },
  { month: '2026-10', sales: 98_450, inflows: 91_720, expenses: 57_340 },
];

export type OrderStatus = 'Opłacone' | 'Częściowo opłacone' | 'Nieopłacone' | 'Anulowane';
export type OrderSource = 'Ręczne' | 'Sklep';

export interface DemoOrder {
  readonly number: string;
  /** `yyyy-MM-dd` */
  readonly date: string;
  readonly customer: string;
  readonly product: string;
  readonly channel: string;
  readonly source: OrderSource;
  readonly value: number;
  readonly paid: number;
  readonly status: OrderStatus;
}

export const ORDERS: readonly DemoOrder[] = [
  {
    number: 'ZAM/2026/0318',
    date: '2026-10-30',
    customer: 'Anna Nowak',
    product: 'Blat granitowy Impala, 3 cm',
    channel: 'Sklep',
    source: 'Sklep',
    value: 7_840,
    paid: 7_840,
    status: 'Opłacone',
  },
  {
    number: 'ZAM/2026/0317',
    date: '2026-10-29',
    customer: 'Piotr Wiśniewski',
    product: 'Parapety marmurowe Carrara ×6',
    channel: 'Telefon',
    source: 'Ręczne',
    value: 3_420,
    paid: 1_000,
    status: 'Częściowo opłacone',
  },
  {
    number: 'ZAM/2026/0316',
    date: '2026-10-28',
    customer: 'Kamil Zieliński',
    product: 'Schody z kwarcytu, 14 stopni',
    channel: 'Znajomy',
    source: 'Ręczne',
    value: 18_960,
    paid: 9_000,
    status: 'Częściowo opłacone',
  },
  {
    number: 'ZAM/2026/0315',
    date: '2026-10-27',
    customer: 'Magdalena Lewandowska',
    product: 'Kostka granitowa 8/11, 4 t',
    channel: 'OLX',
    source: 'Ręczne',
    value: 2_760,
    paid: 2_760,
    status: 'Opłacone',
  },
  {
    number: 'ZAM/2026/0314',
    date: '2026-10-25',
    customer: 'Tomasz Kamiński',
    product: 'Nagrobek pojedynczy, Strzegom',
    channel: 'Sklep',
    source: 'Sklep',
    value: 9_450,
    paid: 0,
    status: 'Nieopłacone',
  },
  {
    number: 'ZAM/2026/0313',
    date: '2026-10-24',
    customer: 'Ewa Dąbrowska',
    product: 'Płytki trawertyn 61×40, 28 m²',
    channel: 'Sklep',
    source: 'Sklep',
    value: 6_120,
    paid: 6_120,
    status: 'Opłacone',
  },
  {
    number: 'ZAM/2026/0312',
    date: '2026-10-22',
    customer: 'Michał Wójcik',
    product: 'Blat kuchenny kwarc Calacatta',
    channel: 'Telefon',
    source: 'Ręczne',
    value: 11_380,
    paid: 11_380,
    status: 'Opłacone',
  },
  {
    number: 'ZAM/2026/0311',
    date: '2026-10-21',
    customer: 'Katarzyna Kozłowska',
    product: 'Otoczaki białe 20–40 mm, 1,5 t',
    channel: 'Sklep',
    source: 'Sklep',
    value: 1_290,
    paid: 0,
    status: 'Anulowane',
  },
  {
    number: 'ZAM/2026/0310',
    date: '2026-10-19',
    customer: 'Robert Jankowski',
    product: 'Palisady bazaltowe ×40',
    channel: 'Inne',
    source: 'Ręczne',
    value: 4_680,
    paid: 4_680,
    status: 'Opłacone',
  },
  {
    number: 'ZAM/2026/0309',
    date: '2026-10-17',
    customer: 'Agnieszka Mazur',
    product: 'Umywalka z onyksu',
    channel: 'Sklep',
    source: 'Sklep',
    value: 2_350,
    paid: 2_350,
    status: 'Opłacone',
  },
  {
    number: 'ZAM/2026/0308',
    date: '2026-10-15',
    customer: 'Paweł Krawczyk',
    product: 'Elewacja piaskowiec, 42 m²',
    channel: 'Znajomy',
    source: 'Ręczne',
    value: 15_720,
    paid: 15_720,
    status: 'Opłacone',
  },
  {
    number: 'ZAM/2026/0307',
    date: '2026-10-12',
    customer: 'Joanna Piotrowska',
    product: 'Kominek marmur Nero Marquina',
    channel: 'Telefon',
    source: 'Ręczne',
    value: 14_450,
    paid: 5_000,
    status: 'Częściowo opłacone',
  },
];

export interface DemoExpense {
  /** `yyyy-MM-dd` */
  readonly date: string;
  readonly description: string;
  readonly category: string;
  readonly amount: number;
  readonly recurring: boolean;
  readonly paid: boolean;
}

export const EXPENSES: readonly DemoExpense[] = [
  {
    date: '2026-10-31',
    description: 'Dostawa granitu — płyty 3 cm',
    category: 'Materiał',
    amount: 21_480,
    recurring: false,
    paid: false,
  },
  {
    date: '2026-10-28',
    description: 'Transport HDS — schody',
    category: 'Transport',
    amount: 1_850,
    recurring: false,
    paid: true,
  },
  {
    date: '2026-10-25',
    description: 'Tarcze diamentowe ×12',
    category: 'Narzędzia',
    amount: 2_940,
    recurring: false,
    paid: true,
  },
  {
    date: '2026-10-20',
    description: 'Kampania reklamowa w wyszukiwarce',
    category: 'Marketing',
    amount: 1_600,
    recurring: true,
    paid: true,
  },
  {
    date: '2026-10-15',
    description: 'Marmur Carrara — slaby',
    category: 'Materiał',
    amount: 16_720,
    recurring: false,
    paid: true,
  },
  {
    date: '2026-10-10',
    description: 'Czynsz za plac i halę',
    category: 'Najem',
    amount: 6_500,
    recurring: true,
    paid: true,
  },
  {
    date: '2026-10-10',
    description: 'Biuro rachunkowe',
    category: 'Usługi',
    amount: 850,
    recurring: true,
    paid: true,
  },
  {
    date: '2026-10-08',
    description: 'Paliwo — samochód dostawczy',
    category: 'Transport',
    amount: 1_420,
    recurring: false,
    paid: true,
  },
  {
    date: '2026-10-05',
    description: 'Leasing wózka widłowego',
    category: 'Leasing',
    amount: 2_380,
    recurring: true,
    paid: true,
  },
  {
    date: '2026-10-03',
    description: 'Energia elektryczna',
    category: 'Media',
    amount: 1_600,
    recurring: true,
    paid: true,
  },
];

export const CURRENT_MONTH = MONTHS[MONTHS.length - 1];
export const PREVIOUS_MONTH = MONTHS[MONTHS.length - 2];
