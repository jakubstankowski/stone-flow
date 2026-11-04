// Proxy serwera deweloperskiego: frontend i API pod jednym originem (`/api/*`), więc
// ciasteczko sesji jest ciasteczkiem pierwszej strony, a CORS nie jest potrzebny.
// Adres API można zmienić zmienną STONEFLOW_API_URL (np. https://localhost:7080).
export default {
  '/api': {
    target: process.env.STONEFLOW_API_URL ?? 'http://localhost:5080',
    secure: false,
  },
};
