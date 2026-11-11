// Unikalny znacznik danych jednego testu: rekordy testu da się znaleźć wyszukiwaniem, a dane z
// wcześniejszych przebiegów (lokalna baza wielokrotnego użytku, ponowienie w CI) nie przeszkadzają.

const LETTERS = 'abcdefghijklmnopqrstuvwxyz0123456789';

export function uniqueTag(): string {
  let suffix = '';
  for (let i = 0; i < 4; i++) {
    suffix += LETTERS[Math.floor(Math.random() * LETTERS.length)];
  }
  return `e2e-${Date.now().toString(36)}-${suffix}`;
}
