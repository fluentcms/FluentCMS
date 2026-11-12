import { FluentCmsProblem } from "./problem.js";
import { toQueryString, type Query } from "./query.js";
import type { Entry, EntryPage, Input, PublishResult } from "./types.js";

export interface ClientOptions {
  // The address of the instance, such as https://cms.example.com/.
  baseUrl: string | URL;
  // The headers of every request, such as the key or the bearer token: { Authorization: "ApiKey ..." }.
  headers?: Record<string, string> | (() => Record<string, string> | Promise<Record<string, string>>);
  fetch?: typeof fetch;
}

export interface WriteOptions {
  // The locale of the write; the default locale of the site when left out.
  locale?: string;
}

const delivery = "api/delivery/v1/";
const management = "api/management/v1/content/";
const systemFields = ["id", "status", "locale", "version", "createdAt", "createdBy", "updatedAt", "updatedBy", "publishedAt", "fallbacks"];

// The Delivery reads and the content group of the Management API over fetch. An answer that is not a success throws
// FluentCmsProblem.
export class FluentCmsClient {
  readonly #baseUrl: URL;
  readonly #headers: ClientOptions["headers"];
  readonly #fetch: typeof fetch;

  constructor(options: ClientOptions) {
    const base = new URL(options.baseUrl);
    if (!base.pathname.endsWith("/")) {
      base.pathname += "/";
    }

    this.#baseUrl = base;
    this.#headers = options.headers;
    this.#fetch = options.fetch ?? globalThis.fetch.bind(globalThis);
  }

  list<T extends Entry>(type: string, query?: Query): Promise<EntryPage<T>> {
    return this.#send<EntryPage<T>>("GET", delivery + segment(type), query);
  }

  get<T extends Entry>(type: string, id: string, query?: Query): Promise<T> {
    return this.#send<T>("GET", `${delivery}${segment(type)}/${segment(id)}`, query);
  }

  // By a unique field, such as a slug.
  getBy<T extends Entry>(type: string, field: string, value: string, query?: Query): Promise<T> {
    return this.#send<T>("GET", `${delivery}${segment(type)}/by/${segment(field)}/${segment(value)}`, query);
  }

  // A singleton's list route answers its one entry.
  getSingleton<T extends Entry>(type: string, query?: Query): Promise<T> {
    return this.#send<T>("GET", delivery + segment(type), query);
  }

  create<T extends Entry>(type: string, entry: Input<T>, options?: WriteOptions): Promise<T> {
    return this.#send<T>("POST", management + segment(type), locale(options), body(entry));
  }

  // Replaces the draft at the version read: a field left out is cleared, and another version is a conflict (412).
  update<T extends Entry>(type: string, id: string, entry: Input<T>, version: number, options?: WriteOptions): Promise<T> {
    return this.#send<T>("PUT", `${management}${segment(type)}/${segment(id)}`, locale(options), body(entry), version);
  }

  // Moves the entry to the trash, in every locale.
  async delete(type: string, id: string): Promise<void> {
    await this.#send<unknown>("DELETE", `${management}${segment(type)}/${segment(id)}`);
  }

  publish<T extends Entry>(type: string, id: string, options?: WriteOptions): Promise<PublishResult<T>> {
    return this.#send<PublishResult<T>>("POST", `${management}${segment(type)}/${segment(id)}/publish`, locale(options));
  }

  unpublish<T extends Entry>(type: string, id: string, options?: WriteOptions): Promise<T> {
    return this.#send<T>("POST", `${management}${segment(type)}/${segment(id)}/unpublish`, locale(options));
  }

  collection<T extends Entry>(type: string): CollectionClient<T> {
    return new CollectionClient<T>(this, type);
  }

  singleton<T extends Entry>(type: string): SingletonClient<T> {
    return new SingletonClient<T>(this, type);
  }

  async #send<T>(method: string, path: string, query?: Query, content?: Record<string, unknown>, version?: number): Promise<T> {
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

    const response = await this.#fetch(url, init);
    const text = await response.text();
    if (!response.ok) {
      throw new FluentCmsProblem(response.status, parse(text));
    }

    return (text.length > 0 ? JSON.parse(text) : undefined) as T;
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

  list(query?: Query): Promise<EntryPage<T>> { return this.#client.list<T>(this.type, query); }
  get(id: string, query?: Query): Promise<T> { return this.#client.get<T>(this.type, id, query); }
  getBy(field: string, value: string, query?: Query): Promise<T> { return this.#client.getBy<T>(this.type, field, value, query); }
  create(entry: Input<T>, options?: WriteOptions): Promise<T> { return this.#client.create<T>(this.type, entry, options); }
  update(id: string, entry: Input<T>, version: number, options?: WriteOptions): Promise<T> { return this.#client.update<T>(this.type, id, entry, version, options); }
  delete(id: string): Promise<void> { return this.#client.delete(this.type, id); }
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

  get(query?: Query): Promise<T> { return this.#client.getSingleton<T>(this.type, query); }
  create(entry: Input<T>, options?: WriteOptions): Promise<T> { return this.#client.create<T>(this.type, entry, options); }
  update(id: string, entry: Input<T>, version: number, options?: WriteOptions): Promise<T> { return this.#client.update<T>(this.type, id, entry, version, options); }
  publish(id: string, options?: WriteOptions): Promise<PublishResult<T>> { return this.#client.publish<T>(this.type, id, options); }
  unpublish(id: string, options?: WriteOptions): Promise<T> { return this.#client.unpublish<T>(this.type, id, options); }
}

function segment(value: string): string {
  if (!value) {
    throw new TypeError("A path segment is empty.");
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

function parse(text: string): unknown {
  try {
    return text.length > 0 ? JSON.parse(text) : undefined;
  } catch {
    return undefined;
  }
}
