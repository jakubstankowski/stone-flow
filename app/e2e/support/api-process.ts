// Proces API dla E2E: zbudowany StoneFlow.Api.dll (Release) uruchamiany bezpośrednio przez
// `dotnet <dll>` (bez `dotnet run`, którego proces potomny mógłby zostać po zatrzymaniu i zająć port).
// Wyjście trafia tylko do bufora w pamięci: link resetu hasła nie jest wypisywany ani zapisywany.
import { ChildProcess, spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { API_PORT, API_URL } from './env';

const BUFFER_LIMIT = 2 * 1024 * 1024;
const API_DLL = resolve(
  __dirname,
  '..',
  '..',
  '..',
  'api',
  'src',
  'StoneFlow.Api',
  'bin',
  'Release',
  'net10.0',
  'StoneFlow.Api.dll',
);
// Log API jest w JSON (AddJsonConsole): `&` w linku ma tam postać sekwencji ucieczki JSON (u0026).
const RESET_LINK = /reset-hasla\/nowe#konto=([0-9a-f-]{36})(?:&|\\u0026)token=([^\s"'\\]+)/;

export interface ApiSettings {
  readonly connectionString: string;
  readonly launchDate: string;
  readonly ownerEmail: string;
  readonly appUrl: string;
}

export interface ResetLink {
  readonly accountId: string;
  readonly token: string;
}

export class ApiProcess {
  private output = '';
  private exited = false;

  private constructor(private readonly child: ChildProcess) {
    const append = (chunk: Buffer) => {
      this.output = (this.output + chunk.toString('utf8')).slice(-BUFFER_LIMIT);
    };
    child.stdout?.on('data', append);
    child.stderr?.on('data', append);
    child.on('exit', () => (this.exited = true));
  }

  /** Uruchamia API i czeka na `/health` = 200 (do 120 s). */
  static async start(settings: ApiSettings): Promise<ApiProcess> {
    if (await respondsAt(`${API_URL}/health`)) {
      throw new Error(
        `Port ${API_PORT} jest zajęty — zatrzymaj lokalne API albo ustaw STONEFLOW_E2E_API_PORT. ` +
          'E2E nie może pisać do bazy deweloperskiej.',
      );
    }
    if (!existsSync(API_DLL)) {
      throw new Error(`Brak ${API_DLL} — zbuduj API: dotnet build stone-flow.slnx -c Release`);
    }
    const child = spawn('dotnet', [API_DLL], {
      cwd: dirname(API_DLL),
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: 'Development',
        DOTNET_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: API_URL,
        ConnectionStrings__StoneFlow: settings.connectionString,
        StoneFlow__LaunchDate: settings.launchDate,
        StoneFlow__Owner__Email: settings.ownerEmail,
        StoneFlow__App__BaseUrl: settings.appUrl,
        StoneFlow__Email__Mode: 'Log',
        Scheduling__Enabled: 'false',
        Logging__LogLevel__Default: 'Information',
      },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    const api = new ApiProcess(child);
    try {
      await api.waitForHealth(120_000);
    } catch (error) {
      await api.stop();
      throw error;
    }
    return api;
  }

  /** Długość bufora — punkt odniesienia dla `waitForResetLink`. */
  get outputLength(): number {
    return this.output.length;
  }

  /** Link resetu z logu API (zdarzenie 6051) zapisany po pozycji `after` bufora. */
  async waitForResetLink(timeoutMs: number, after = 0): Promise<ResetLink> {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      const match = RESET_LINK.exec(this.output.slice(after));
      if (match) {
        return { accountId: match[1], token: decodeURIComponent(match[2]) };
      }
      await delay(250);
    }
    throw new Error(
      'Link resetu nie pojawił się w logu API (zdarzenie 6051). Konto dostaje najwyżej 3 linki na ' +
        '15 minut — przy częstych przebiegach na tej samej bazie odczekaj albo odtwórz bazę StoneFlowE2E.',
    );
  }

  async stop(): Promise<void> {
    if (this.exited) {
      return;
    }
    const exited = new Promise<void>((done) => this.child.once('exit', () => done()));
    this.child.kill();
    const timer = setTimeout(() => this.child.kill('SIGKILL'), 5_000);
    await exited;
    clearTimeout(timer);
  }

  private async waitForHealth(timeoutMs: number): Promise<void> {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      if (this.exited) {
        throw new Error(`API zakończyło działanie przed startem:\n${this.tail(50)}`);
      }
      if (await respondsAt(`${API_URL}/health`, 200)) {
        return;
      }
      await delay(1_000);
    }
    throw new Error(`API nie odpowiedziało na /health w ${timeoutMs / 1000} s:\n${this.tail(50)}`);
  }

  private tail(lines: number): string {
    return this.output.split(/\r?\n/).slice(-lines).join('\n');
  }
}

async function respondsAt(url: string, status?: number): Promise<boolean> {
  try {
    const response = await fetch(url, { signal: AbortSignal.timeout(2_000) });
    return status === undefined || response.status === status;
  } catch {
    return false;
  }
}

function delay(ms: number): Promise<void> {
  return new Promise((done) => setTimeout(done, ms));
}
