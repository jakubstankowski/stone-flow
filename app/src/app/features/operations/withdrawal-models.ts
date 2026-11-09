/** Metoda wypłaty właściciela (kod API). */
export type WithdrawalMethod = 'transfer' | 'blik' | 'cash';

/** Treść zapisu (`POST`, `PUT`): kwota jako ciąg dziesiętny z kropką (`"2000.00"`), data `yyyy-MM-dd`. */
export interface WithdrawalInput {
  readonly person: string;
  readonly amount: string;
  readonly withdrawnOn: string;
  readonly method: WithdrawalMethod;
}

export interface WithdrawalDetails {
  readonly id: string;
  readonly person: string;
  readonly amount: string;
  readonly withdrawnOn: string;
  readonly method: WithdrawalMethod;
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
  readonly version: string;
}

export interface WithdrawalCreated {
  readonly id: string;
  readonly version: string;
}

export interface WithdrawalVersion {
  readonly version: string;
}

/** Kody Problem Details wypłaty, na których rozgałęzia się interfejs. */
export const WITHDRAWAL_PROBLEM = {
  notFound: 'owner_withdrawal_not_found',
  conflict: 'owner_withdrawal_conflict',
  keyReused: 'idempotency_key_reused',
} as const;

export const WITHDRAWAL_METHODS: readonly WithdrawalMethod[] = ['transfer', 'blik', 'cash'];

export const WITHDRAWAL_METHOD_LABELS: Readonly<Record<WithdrawalMethod, string>> = {
  transfer: 'Przelew',
  blik: 'BLIK',
  cash: 'Gotówka',
};
