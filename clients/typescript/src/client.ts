import { FluentCmsProblem } from "./problem.js";
import { toQueryString, type Query } from "./query.js";
import type { Entry, EntryPage, Input, PublishResult } from "./types.js";

export interface ClientOptions {
  // The address of the instance, such as https://cms.example.com/.
  baseUrl: string | URL;
  // The headers of every request, such as the key or the bearer token: { Authorization: "ApiKey ..." }.
  headers?: Record<string, string> | (() => Record<string, string> | Promise<Record<string, string>>);
  fetch?: typeof fetch;
  // Aborts a request, and the read of its answer, after this many milliseconds; a request has no limit when left out.
  timeoutMs?: number;
}

export interface RequestOptions {
  // Aborts the request, and the read of its answer.
  signal?: AbortSignal;
}

export interface WriteOptions extends RequestOptions {
  // The locale of the write; the default locale of the site when left out.
  locale?: string;
}

const delivery = "api/delivery/v1/";
const management = "api/management/v1/content/";
// The longest delay setTimeout keeps; a longer one would fire at once.
const maxTimeoutMs = 2_147_483_647;
const systemFields = ["id", "status", "locale", "version", "createdAt", "createdBy", "updatedAt", "updatedBy", "publishedAt", "scheduledPublishAt", "scheduledUnpublishAt", "fallbacks"];

// The Delivery reads and the content group of the Management API over fetch. An answer that is not a success throws
// FluentCmsProblem.
export class FluentCmsClient {
  readonly #baseUrl: URL;
  readonly #headers: ClientOptions["headers"];
  readonly #fetch: typeof fetch;
  readonly #timeoutMs: number | undefined;

  constructor(options: ClientOptions) {
    const timeoutMs = options.timeoutMs;
    if (timeoutMs !== undefined && !(Number.isFinite(timeoutMs) && timeoutMs > 0 && timeoutMs <= maxTimeoutMs)) {
      throw new RangeError(`timeoutMs must be a number of milliseconds above 0 and at most ${maxTimeoutMs}.`);
    }

    const base = new URL(options.baseUrl);
    if (!base.pathname.endsWith("/")) {
      base.pathname += "/";
    }

    this.#baseUrl = base;
    this.#headers = options.headers;
    this.#fetch = options.fetch ?? globalThis.fetch.bind(globalThis);
    this.#timeoutMs = timeoutMs;
  }

  async list<T extends Entry>(type: string, query?: Query, options?: RequestOptions): Promise<EntryPage<T>> {
    return await this.#result<EntryPage<T>>("GET", delivery + segment(type), query, options);
  }

  async get<T extends Entry>(type: string, id: string, query?: Query, options?: RequestOptions): Promise<T> {
    return await this.#result<T>("GET", `${delivery}${segment(type)}/${segment(id)}`, query, options);
  }

  // By a unique field, such as a slug.
  async getBy<T extends Entry>(type: string, field: string, value: string, query?: Query, options?: RequestOptions): Promise<T> {
    return await this.#result<T>("GET", `${delivery}${segment(type)}/by/${segment(field)}/${segment(value)}`, query, options);
  }

  // A singleton's list route answers its one entry.
  async getSingleton<T extends Entry>(type: string, query?: Query, options?: RequestOptions): Promise<T> {
    return await this.#result<T>("GET", delivery + segment(type), query, options);
  }

  async create<T extends Entry>(type: string, entry: Input<T>, options?: WriteOptions): Promise<T> {
    return await this.#result<T>("POST", management + segment(type), locale(options), options, body(entry));
  }

  // Replaces the draft at the version read: a field left out is cleared, and another version is a conflict (412).
  async update<T extends Entry>(type: string, id: string, entry: Input<T>, version: number, options?: WriteOptions): Promise<T> {
    return await this.#result<T>("PUT", `${management}${segment(type)}/${segment(id)}`, locale(options), options, body(entry), version);
  }

  // Moves the entry to the trash, in every locale.
  async delete(type: string, id: string, options?: RequestOptions): Promise<void> {
    await this.#send("DELETE", `${management}${segment(type)}/${segment(id)}`, undefined, options);
  }

  async publish<T extends Entry>(type: string, id: string, options?: WriteOptions): Promise<PublishResult<T>> {
    return await this.#result<PublishResult<T>>("POST", `${management}${segment(type)}/${segment(id)}/publish`, locale(options), options);
  }

  async unpublish<T extends Entry>(type: string, id: string, options?: WriteOptions): Promise<T> {
    return await this.#result<T>("POST", `${management}${segment(type)}/${segment(id)}/unpublish`, locale(options), options);
  }

  collection<T extends Entry>(type: string): CollectionClient<T> {
    return new CollectionClient<T>(this, type);
  }

  singleton<T extends Entry>(type: string): SingletonClient<T> {
    return new SingletonClient<T>(this, type);
  }

  // An answer that names a result: an empty one, a 204 or a literal null is a problem, as a body that is not JSON is.
  async #result<T>(method: string, path: string, query: Query | undefined, options: RequestOptions | undefined, content?: Record<string, unknown>, version?: number): Promise<T> {
    const answer = await this.#send(method, path, query, options, content, version);
    if (answer.empty) {
      throw new FluentCmsProblem(answer.status, { detail: "The answer is empty." });
    }

    if (answer.value === null) {
      throw new FluentCmsProblem(answer.status, { detail: "The answer is null." });
    }

    return answer.value as T;
  }

  async #send(method: string, path: string, query: Query | undefined, options: RequestOptions | undefined, content?: Record<string, unknown>, version?: number): Promise<{ status: number; empty: boolean; value: unknown }> {
    const queryString = query ? toQueryString(query) : "";
    const url = new URL(queryString.length > 0 ? `${path}?${queryString}` : path, this.#baseUrl);
    const headers: Record<string, string> = { Accept: "application/json", ...(await this.#resolveHeaders()) };
    if (content !== undefined) {
      headers["Content-Type"] = "application/json";
    }

    if (version !== undefined) {
      headers["If-Match"] = `"${version}"`;
    }

    const init: RequestInit = { method, headers };
    if (content !== undefined) {
      init.body = JSON.stringify(content);
    }

    const limit = this.#limit(options?.signal);
    if (limit.signal !== undefined) {
      init.signal = limit.signal;
    }

    let response: Response;
    let text: string;
    try {
      response = await this.#fetch(url, init);
      text = await response.text();
    } finally {
      limit.end();
    }

    if (!response.ok) {
      throw new FluentCmsProblem(response.status, parse(text), retryAfter(response.headers));
    }

    if (text.length === 0 || response.status === 204) {
      return { status: response.status, empty: true, value: undefined };
    }

    // A body this client cannot read, such as a page of a proxy, is a problem with no code, as an answer that is not one is.
    try {
      return { status: response.status, empty: false, value: JSON.parse(text) };
    } catch {
      throw new FluentCmsProblem(response.status, { detail: "The answer is not JSON." });
    }
  }

  // The caller's signal, or one that also aborts at timeoutMs; end() stops the timer and lets go of the caller's signal.
  #limit(signal: AbortSignal | undefined): { signal: AbortSignal | undefined; end: () => void } {
    const timeoutMs = this.#timeoutMs;
    if (timeoutMs === undefined) {
      return { signal, end: () => {} };
    }

    const controller = new AbortController();
    const forward = () => controller.abort(signal?.reason);
    if (signal?.aborted) {
      forward();
    } else {
      signal?.addEventListener("abort", forward, { once: true });
    }

    const timer = setTimeout(() => controller.abort(new DOMException(`The request did not complete within ${timeoutMs} ms.`, "TimeoutError")), timeoutMs);
    return {
      signal: controller.signal,
      end: () => {
        clearTimeout(timer);
        signal?.removeEventListener("abort", forward);
      },
    };
  }

  async #resolveHeaders(): Promise<Record<string, string>> {
    const headers = this.#headers;
    return typeof headers === "function" ? await headers() : headers ?? {};
  }
}

// The calls of one collection, which createClient of the generated module binds to its type.
export class CollectionClient<T extends Entry> {
  readonly #client: FluentCmsClient;
  readonly type: string;

  constructor(client: FluentCmsClient, type: string) {
    this.#client = client;
    this.type = type;
  }

  list(query?: Query, options?: RequestOptions): Promise<EntryPage<T>> { return this.#client.list<T>(this.type, query, options); }
  get(id: string, query?: Query, options?: RequestOptions): Promise<T> { return this.#client.get<T>(this.type, id, query, options); }
  getBy(field: string, value: string, query?: Query, options?: RequestOptions): Promise<T> { return this.#client.getBy<T>(this.type, field, value, query, options); }
  create(entry: Input<T>, options?: WriteOptions): Promise<T> { return this.#client.create<T>(this.type, entry, options); }
  update(id: string, entry: Input<T>, version: number, options?: WriteOptions): Promise<T> { return this.#client.update<T>(this.type, id, entry, version, options); }
  delete(id: string, options?: RequestOptions): Promise<void> { return this.#client.delete(this.type, id, options); }
  publish(id: string, options?: WriteOptions): Promise<PublishResult<T>> { return this.#client.publish<T>(this.type, id, options); }
  unpublish(id: string, options?: WriteOptions): Promise<T> { return this.#client.unpublish<T>(this.type, id, options); }
}

export class SingletonClient<T extends Entry> {
  readonly #client: FluentCmsClient;
  readonly type: string;

  constructor(client: FluentCmsClient, type: string) {
    this.#client = client;
    this.type = type;
  }

  get(query?: Query, options?: RequestOptions): Promise<T> { return this.#client.getSingleton<T>(this.type, query, options); }
  create(entry: Input<T>, options?: WriteOptions): Promise<T> { return this.#client.create<T>(this.type, entry, options); }
  update(id: string, entry: Input<T>, version: number, options?: WriteOptions): Promise<T> { return this.#client.update<T>(this.type, id, entry, version, options); }
  publish(id: string, options?: WriteOptions): Promise<PublishResult<T>> { return this.#client.publish<T>(this.type, id, options); }
  unpublish(id: string, options?: WriteOptions): Promise<T> { return this.#client.unpublish<T>(this.type, id, options); }
}

// . and .. would be removed when the address is resolved and reach another route; every other value is escaped, % too.
function segment(value: string): string {
  if (!value) {
    throw new TypeError("A path segment is empty.");
  }

  if (value === "." || value === "..") {
    throw new TypeError(`'${value}' cannot be a path segment.`);
  }

  return encodeURIComponent(value);
}

function locale(options: WriteOptions | undefined): Query | undefined {
  return options?.locale === undefined ? undefined : { locale: options.locale };
}

// The fields of the entry; the system fields are the server's.
function body(entry: object): Record<string, unknown> {
  const fields: Record<string, unknown> = { ...(entry as Record<string, unknown>) };
  for (const name of systemFields) {
    delete fields[name];
  }

  return fields;
}

// Seconds as the API sends them, or a date, which is read against the Date of the same answer so that no clock of this
// process is involved. A date has the name of its month; any other value that is not a whole number of seconds, such as
// -5 or 1.5, gives nothing, as a date with no Date header does, rather than what Date.parse would make of it.
function retryAfter(headers: Headers): number | undefined {
  const value = headers.get("Retry-After")?.trim();
  if (value === undefined || value.length === 0) {
    return undefined;
  }

  if (/^\d+$/.test(value)) {
    return Number(value);
  }

  if (!/[a-z]/i.test(value)) {
    return undefined;
  }

  const at = Date.parse(value);
  const now = Date.parse(headers.get("Date") ?? "");
  return Number.isNaN(at) || Number.isNaN(now) ? undefined : Math.max(0, (at - now) / 1000);
}

function parse(text: string): unknown {
  try {
    return text.length > 0 ? JSON.parse(text) : undefined;
  } catch {
    return undefined;
  }
}
