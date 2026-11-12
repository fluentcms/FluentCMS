import type { FieldFilter, Filter, FilterValue, SortField } from "./filters.js";

// A read of the Delivery API: the bracket filters of the typed builders, sort, paging, populate, locale and status.
export interface Query {
  filter?: Filter;
  sort?: SortField | readonly SortField[];
  page?: number;
  pageSize?: number;
  cursor?: string;
  fields?: readonly string[];
  populate?: readonly string[];
  depth?: number;
  status?: "published" | "draft";
  locale?: string;
}

// The query string of spec 4.4, in its order and with its escaping, without the leading "?". FluentCMS.Client writes the
// same: a list operand is comma separated, with \, and \\ for a comma and a backslash in a value.
export function toQueryString(query: Query): string {
  const pairs: [string, string][] = [];
  if (query.filter) {
    writeFilter(query.filter, "filter", pairs);
  }

  const sort = query.sort === undefined ? [] : Array.isArray(query.sort) ? query.sort : [query.sort as SortField];
  if (sort.length > 0) {
    pairs.push(["sort", sort.map((field) => (field.descending ? "-" : "") + field.path).join(",")]);
  }

  add("page", query.page);
  add("pageSize", query.pageSize);
  add("cursor", query.cursor);
  add("fields", query.fields && query.fields.length > 0 ? query.fields.join(",") : undefined);
  add("populate", query.populate && query.populate.length > 0 ? query.populate.join(",") : undefined);
  add("depth", query.depth);
  add("status", query.status);
  add("locale", query.locale);

  return pairs.map(([key, value]) => `${escapeKey(key)}=${escapeValue(value)}`).join("&");

  function add(key: string, value: string | number | undefined): void {
    if (value !== undefined) {
      pairs.push([key, String(value)]);
    }
  }
}

function writeFilter(filter: Filter, prefix: string, pairs: [string, string][]): void {
  switch (filter.kind) {
    case "field":
      if ((filter.operator === "in" || filter.operator === "nin" || filter.operator === "between")
        && (filter.value === undefined || (Array.isArray(filter.value) && filter.value.length === 0))) {
        throw new TypeError(`The filter on '${filter.path}' needs at least one value.`);
      }

      pairs.push([`${prefix}[${filter.path}][${filter.operator}]`, operand(filter)]);
      break;
    case "not":
      writeFilter(filter.filter, `${prefix}[not]`, pairs);
      break;
    case "or":
      if (filter.filters.length === 0) {
        throw new TypeError("An and or an or needs at least one condition.");
      }

      filter.filters.forEach((item, index) => writeFilter(item, `${prefix}[or][${index}]`, pairs));
      break;
    case "and":
      if (filter.filters.length === 0) {
        throw new TypeError("An and or an or needs at least one condition.");
      }

      if (needsIndexes(filter.filters)) {
        filter.filters.forEach((item, index) => writeFilter(item, `${prefix}[and][${index}]`, pairs));
      } else {
        filter.filters.forEach((item) => writeFilter(item, prefix, pairs));
      }

      break;
  }
}

// Conditions side by side are an implicit and, unless their keys would merge when read back.
function needsIndexes(filters: readonly Filter[]): boolean {
  const count = (kind: Filter["kind"]) => filters.filter((item) => item.kind === kind).length;
  const keys = filters.filter((item): item is FieldFilter => item.kind === "field").map((item) => `${item.path}\u0000${item.operator}`);
  return count("and") > 0 || count("or") > 1 || count("not") > 1 || new Set(keys).size !== keys.length;
}

function operand(filter: FieldFilter): string {
  if (filter.operator === "null" || filter.operator === "notNull") {
    return "true";
  }

  const value = filter.value;
  if (Array.isArray(value)) {
    return value.map((item: FilterValue) => escapeItem(text(item))).join(",");
  }

  const single = value as FilterValue | undefined;
  return filter.operator === "in" || filter.operator === "nin" || filter.operator === "between" ? escapeItem(text(single)) : text(single);
}

// A Date in UTC with Z and no trailing zeros in its fraction, which a DateTimeOffset also gives in C#.
function text(value: FilterValue | undefined): string {
  if (value === undefined) {
    return "";
  }

  if (value instanceof Date) {
    const iso = value.toISOString();
    const fraction = iso.slice(20, 23).replace(/0+$/, "");
    return iso.slice(0, 19) + (fraction.length > 0 ? `.${fraction}` : "") + "Z";
  }

  return typeof value === "boolean" ? (value ? "true" : "false") : String(value);
}

function escapeItem(value: string): string {
  return value.replaceAll("\\", "\\\\").replaceAll(",", "\\,");
}

// encodeURIComponent leaves !'()* alone, which Uri.EscapeDataString in C# escapes; the grammar keeps , [ ] and *.
function escape(value: string): string {
  return encodeURIComponent(value).replace(/[!'()*]/g, (character) => "%" + character.charCodeAt(0).toString(16).toUpperCase());
}

function escapeKey(key: string): string {
  return escape(key).replaceAll("%5B", "[").replaceAll("%5D", "]");
}

function escapeValue(value: string): string {
  return escape(value).replaceAll("%2C", ",").replaceAll("%2A", "*");
}
