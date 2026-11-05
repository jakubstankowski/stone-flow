import { NETWORK_ERROR_MESSAGE, ApiError } from '../../core/http/api-error';
import { parsePlnAmount } from '../../shared/format/pl-format';
import {
  CustomerKind,
  FulfillmentStatus,
  OrderChannel,
  OrderDetails,
  OrderInput,
  ShopOwnedField,
} from './order-models';

/** Model formularza: wszystkie pola tekstowe jako tekst, listy jako jedna pozycja w wierszu. */
export interface OrderFormModel {
  orderDate: string;
  channel: OrderChannel | '';
  value: string;
  fulfillmentStatus: FulfillmentStatus;
  customer: {
    kind: CustomerKind;
    name: string;
    phone: string;
    email: string;
    street: string;
    postalCode: string;
    city: string;
    taxId: string;
  };
  operational: {
    suppliers: string;
    zoReferences: string;
    invoiceNumber: string;
    hasPhotos: boolean;
    hasReview: boolean;
    notes: string;
  };
  comment: string;
}

export const LIMITS = {
  name: 200,
  phone: 32,
  email: 254,
  street: 200,
  city: 100,
  listItems: 10,
  supplier: 100,
  zoReference: 64,
  invoiceNumber: 64,
  notes: 2000,
  comment: 2000,
  /** Cyfry części całkowitej w `decimal(18,2)`. */
  amountIntegerDigits: 16,
} as const;

export const ORDER_MESSAGES = {
  nameRequired: 'Podaj imię i nazwisko lub nazwę firmy.',
  nameTooLong: `Nazwa może mieć najwyżej ${LIMITS.name} znaków.`,
  phoneTooLong: `Telefon może mieć najwyżej ${LIMITS.phone} znaki.`,
  emailInvalid: 'Podaj poprawny adres e-mail.',
  streetTooLong: `Adres może mieć najwyżej ${LIMITS.street} znaków.`,
  postalCodeInvalid: 'Podaj kod pocztowy w formacie 00-000.',
  cityTooLong: `Miejscowość może mieć najwyżej ${LIMITS.city} znaków.`,
  taxIdInvalid: 'Podaj poprawny NIP: 10 cyfr z prawidłową sumą kontrolną.',
  orderDateRequired: 'Podaj datę zamówienia.',
  channelRequired: 'Wybierz kanał.',
  valueRequired: 'Podaj wartość brutto.',
  valueInvalid: 'Podaj kwotę nieujemną z najwyżej dwoma miejscami po przecinku, np. 1 234,50.',
  valueTooLarge: 'Kwota jest zbyt duża.',
  suppliersTooMany: `Podaj najwyżej ${LIMITS.listItems} dostawców.`,
  supplierTooLong: `Każdy dostawca może mieć najwyżej ${LIMITS.supplier} znaków.`,
  zoTooMany: `Podaj najwyżej ${LIMITS.listItems} referencji ZO.`,
  zoTooLong: `Każda referencja ZO może mieć najwyżej ${LIMITS.zoReference} znaki.`,
  invoiceTooLong: `Numer faktury może mieć najwyżej ${LIMITS.invoiceNumber} znaki.`,
  notesTooLong: `Uwagi mogą mieć najwyżej ${LIMITS.notes} znaków.`,
  commentTooLong: `Komentarz może mieć najwyżej ${LIMITS.comment} znaków.`,
  saveFailed: 'Nie udało się zapisać zamówienia.',
  csrf: 'Odśwież stronę i spróbuj ponownie.',
} as const;

const POSTAL_CODE = /^\d{2}-\d{3}$/;
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const NIP_WEIGHTS = [6, 5, 7, 2, 3, 4, 5, 6, 7];

/** Dzisiejsza data w strefie Europe/Warsaw jako `yyyy-MM-dd`. */
export function todayInWarsaw(now = new Date()): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Europe/Warsaw',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(now);
}

export function emptyOrderForm(orderDate = todayInWarsaw()): OrderFormModel {
  return {
    orderDate,
    channel: '',
    value: '',
    fulfillmentStatus: 'inProgress',
    customer: {
      kind: 'person',
      name: '',
      phone: '',
      email: '',
      street: '',
      postalCode: '',
      city: '',
      taxId: '',
    },
    operational: {
      suppliers: '',
      zoReferences: '',
      invoiceNumber: '',
      hasPhotos: false,
      hasReview: false,
      notes: '',
    },
    comment: '',
  };
}

/** Stan z serwera → formularz. Kwota `1234.50` staje się `1234,50` (zamiana znaku, bez liczenia). */
export function orderFormFrom(order: OrderDetails): OrderFormModel {
  const { customer, operational } = order;
  return {
    orderDate: order.orderDate,
    channel: order.channel,
    value: order.value.replace('.', ','),
    fulfillmentStatus: order.fulfillmentStatus,
    customer: {
      kind: customer.kind,
      name: customer.name,
      phone: customer.phone ?? '',
      email: customer.email ?? '',
      street: customer.street ?? '',
      postalCode: customer.postalCode ?? '',
      city: customer.city ?? '',
      taxId: customer.taxId ?? '',
    },
    operational: {
      suppliers: operational.suppliers.join('\n'),
      zoReferences: operational.zoReferences.join('\n'),
      invoiceNumber: operational.invoiceNumber ?? '',
      hasPhotos: operational.hasPhotos,
      hasReview: operational.hasReview,
      notes: operational.notes ?? '',
    },
    comment: order.comment ?? '',
  };
}

/**
 * Formularz → treść żądania. Teksty są przycinane, puste stają się `null`, listy tracą puste
 * wiersze, kwota idzie jako `"1234.50"`. Pola należące do sklepu są pomijane (API je ignoruje).
 */
export function toOrderInput(model: OrderFormModel, shopOwned: readonly string[] = []): OrderInput {
  const owned = (field: ShopOwnedField) => shopOwned.includes(field);
  const { customer, operational } = model;
  return {
    ...(owned('orderDate') ? {} : { orderDate: model.orderDate }),
    ...(owned('channel') || !model.channel ? {} : { channel: model.channel }),
    ...(owned('value') ? {} : { value: parsePlnAmount(model.value) ?? model.value.trim() }),
    ...(owned('customer')
      ? {}
      : {
          customer: {
            kind: customer.kind,
            name: customer.name.trim(),
            phone: textOrNull(customer.phone),
            email: textOrNull(customer.email),
            street: textOrNull(customer.street),
            postalCode: textOrNull(customer.postalCode),
            city: textOrNull(customer.city),
            taxId: textOrNull(customer.taxId),
          },
        }),
    fulfillmentStatus: model.fulfillmentStatus,
    operational: {
      suppliers: listItems(operational.suppliers),
      zoReferences: listItems(operational.zoReferences),
      invoiceNumber: textOrNull(operational.invoiceNumber),
      hasPhotos: operational.hasPhotos,
      hasReview: operational.hasReview,
      notes: textOrNull(operational.notes),
    },
    comment: textOrNull(model.comment),
  };
}

export function listItems(text: string): string[] {
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

export function textOrNull(text: string): string | null {
  const trimmed = text.trim();
  return trimmed.length > 0 ? trimmed : null;
}

/** Komunikat dla kwoty albo `null`, gdy poprawna. */
export function amountError(text: string): string | null {
  if (text.trim() === '') {
    return ORDER_MESSAGES.valueRequired;
  }
  const amount = parsePlnAmount(text);
  if (amount === null) {
    return ORDER_MESSAGES.valueInvalid;
  }
  const integerDigits = amount.indexOf('.');
  return integerDigits > LIMITS.amountIntegerDigits ? ORDER_MESSAGES.valueTooLarge : null;
}

export function isValidDate(text: string): boolean {
  return ISO_DATE.test(text);
}

export function isValidPostalCode(text: string): boolean {
  const trimmed = text.trim();
  return trimmed === '' || POSTAL_CODE.test(trimmed);
}

export function isValidEmail(text: string): boolean {
  const trimmed = text.trim();
  return trimmed === '' || (trimmed.length <= LIMITS.email && EMAIL.test(trimmed));
}

/** NIP: po usunięciu spacji i myślników 10 cyfr z poprawną sumą kontrolną; puste pole jest poprawne. */
export function isValidTaxId(text: string): boolean {
  const trimmed = text.trim();
  if (trimmed === '') {
    return true;
  }
  if (!/^[\d\s-]+$/.test(trimmed)) {
    return false;
  }
  const digits = trimmed.replace(/[\s-]/g, '');
  if (!/^\d{10}$/.test(digits)) {
    return false;
  }
  const sum = NIP_WEIGHTS.reduce(
    (total, weight, index) => total + weight * Number(digits[index]),
    0,
  );
  const control = sum % 11;
  return control !== 10 && control === Number(digits[9]);
}

/** Komunikat ogólny formularza dla błędu, który nie dotyczy konkretnego pola. */
export function orderErrorMessage(error: ApiError): string {
  if (error.kind === 'network') {
    return NETWORK_ERROR_MESSAGE;
  }
  if (error.kind === 'problem' && error.code === 'csrf_invalid') {
    return ORDER_MESSAGES.csrf;
  }
  return error.detail ?? ORDER_MESSAGES.saveFailed;
}
