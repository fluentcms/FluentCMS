import { readFileSync } from "node:fs";
import assert from "node:assert/strict";
import { test } from "node:test";
import * as client from "@fluentcms/client";
import { and, not, or, toQueryString, type Filter, type Query, type SortField } from "@fluentcms/client";

// clients/filter-cases.json, which FluentCMS.Client's tests read too: both clients must write each case the same way.
interface Case {
  field: string;
  path: string;
  key?: string;
  call: string;
  args?: unknown[];
  query?: string;
}

interface Node {
  and?: Node[];
  or?: Node[];
  not?: Node;
  field?: string;
}

interface QueryCase {
  name: string;
  query: Record<string, unknown> & { filter?: Node; sort?: Case[] };
  expect: string;
}

const cases = JSON.parse(readFileSync(new URL("../../../filter-cases.json", import.meta.url), "utf8")) as { filters: Case[]; queries: QueryCase[] };

function call(item: Case): unknown {
  const Field = (client as unknown as Record<string, (new (path: string) => Record<string, (...args: unknown[]) => unknown>) | undefined>)[item.field];
  if (Field === undefined) {
    throw new Error(`${item.field} is not a field type of @fluentcms/client`);
  }

  let field = new Field(item.path);
  if (item.key !== undefined) {
    field = field["key"]!(item.key) as Record<string, (...args: unknown[]) => unknown>;
  }

  const method = field[item.call];
  if (typeof method !== "function") {
    throw new Error(`${item.field}.${item.call} is not a method`);
  }

  return method.apply(field, item.args ?? []);
}

function node(item: Node): Filter {
  if (item.and) return and(...item.and.map(node));
  if (item.or) return or(...item.or.map(node));
  if (item.not) return not(node(item.not));
  return call(item as Case) as Filter;
}

test("every filter of the shared list gives its query string", () => {
  assert.ok(cases.filters.length > 30);
  for (const item of cases.filters) {
    assert.equal(toQueryString({ filter: call(item) as Filter }), item.query, JSON.stringify(item));
  }
});

test("every query of the shared list gives its query string", () => {
  for (const item of cases.queries) {
    const { filter, sort, ...rest } = item.query;
    const query: Query = { ...(rest as Query) };
    if (filter) query.filter = node(filter);
    if (sort) query.sort = sort.map((field) => call(field) as SortField);
    assert.equal(toQueryString(query), item.expect, item.name);
  }
});

test("a Date is written in UTC without trailing zeros, as a DateTimeOffset is in C#", () => {
  const field = new client.DateField("publishedOn");
  assert.equal(toQueryString({ filter: field.gte(new Date(Date.UTC(2026, 0, 2, 3, 4, 5))) }), "filter[publishedOn][gte]=2026-01-02T03%3A04%3A05Z");
  assert.equal(toQueryString({ filter: field.lt(new Date(Date.UTC(2026, 0, 2, 3, 4, 5, 500))) }), "filter[publishedOn][lt]=2026-01-02T03%3A04%3A05.5Z");
  assert.equal(toQueryString({ filter: new client.NumberField("rating").eq(12345678901234567890n) }), "filter[rating][eq]=12345678901234567890");
});

test("a list operator without a value and an empty and are refused", () => {
  assert.throws(() => toQueryString({ filter: new client.TextField("title").in() }), (error) => error instanceof TypeError);
  assert.throws(() => toQueryString({ filter: and() }), (error) => error instanceof TypeError);
});
