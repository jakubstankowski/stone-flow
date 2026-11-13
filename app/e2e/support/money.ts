// Kwoty z interfejsu („12 345,67 zł”, „-200,00 zł”) jako całkowite grosze — porównania bez
// arytmetyki zmiennoprzecinkowej (invariant 2). `\s` obejmuje też twardą spację separatora grup.

const AMOUNT = /^(-?)(\d+),(\d{2})$/;

export function plnToGrosze(text: string): number {
  const normalized = text.replace(/zł/g, '').replace(/\s/g, '').replace(/−/g, '-');
  const match = AMOUNT.exec(normalized);
  if (!match) {
    throw new Error(`To nie jest kwota w formacie PLN: „${text}”`);
  }
  const value = Number(match[2]) * 100 + Number(match[3]);
  return match[1] === '-' ? -value : value;
}

/** Format kwoty w interfejsie: grupy cyfr, przecinek, dwa miejsca, „zł”. */
export const PLN_FORMAT = /^-?\d{1,3}(\s\d{3})*,\d{2}\szł$/;
