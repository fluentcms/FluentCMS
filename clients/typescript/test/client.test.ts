import assert from "node:assert/strict";
import { test } from "node:test";
import { FluentCmsClient, FluentCmsProblem } from "@fluentcms/client";
import { ArticleFields, createClient, type Article, type HeroBlock } from "./blog/fluentcms.js";

interface Sent {
  url: string;
  method: string;
  headers: Record<string, string>;
  body: string | undefined;
}

function answering(status: number, body: unknown, contentType = "application/json", headers: Record<string, string> = {}): { fetch: typeof fetch; sent: Sent[] } {
  const sent: Sent[] = [];
  const fake = async (input: string | URL | Request, init?: RequestInit): Promise<Response> => {
    sent.push({
      url: String(input),
      method: init?.method ?? "GET",
      headers: (init?.headers ?? {}) as Record<string, string>,
      body: typeof init?.body === "string" ? init.body : undefined,
    });
    return new Response(status === 204 ? null : typeof body === "string" ? body : JSON.stringify(body), { status, headers: { "Content-Type": contentType, ...headers } });
  };

  return { fetch: fake as typeof fetch, sent };
}

test("createClient binds the content types and a typed query reaches the Delivery API", async () => {
  const { fetch, sent } = answering(200, { items: [{ id: "1", title: "Welcome", sections: [{ type: "hero", heading: "Hi" }] }], page: 1, pageSize: 10 });
  const cms = createClient({ baseUrl: "https://cms.example.com/cms", headers: { Authorization: "ApiKey secret" }, fetch });

  const page = await cms.article.list({
    filter: ArticleFields.sections.hero.heading.eq("Hi"),
    sort: ArticleFields.publishedOn.descending(),
    pageSize: 10,
    populate: ["author"],
  });

  assert.equal(sent[0]?.url, "https://cms.example.com/cms/api/delivery/v1/article?filter[sections.hero.heading][eq]=Hi&sort=-publishedOn&pageSize=10&populate=author");
  assert.equal(sent[0]?.headers["Authorization"], "ApiKey secret");
  const article: Article | undefined = page.items[0];
  const block = article?.sections?.[0];
  assert.equal(block?.type === "hero" ? (block satisfies HeroBlock).heading : undefined, "Hi");
});

test("a write sends the fields with the locale and the version, without the system fields", async () => {
  const { fetch, sent } = answering(200, { id: "1", version: 4, scheduledPublishAt: null, scheduledUnpublishAt: "2027-03-01T09:00:00Z" });
  const cms = createClient(new FluentCmsClient({ baseUrl: "https://cms.example.com/", fetch }));

  const updated = await cms.article.update("1", { title: "Hello", author: "0192f2a4-1111-7000-8000-000000000001", sections: [{ type: "quote", text: "Typed." }] }, 3, { locale: "fr" });

  assert.equal(updated.version, 4);
  assert.equal(updated.scheduledPublishAt, null);
  assert.equal(updated.scheduledUnpublishAt, "2027-03-01T09:00:00Z");
  assert.equal(sent[0]?.method, "PUT");
  assert.equal(sent[0]?.url, "https://cms.example.com/api/management/v1/content/article/1?locale=fr");
  assert.equal(sent[0]?.headers["If-Match"], "\"3\"");
  assert.deepEqual(JSON.parse(sent[0]?.body ?? "{}"), { title: "Hello", author: "0192f2a4-1111-7000-8000-000000000001", sections: [{ type: "quote", text: "Typed." }] });

  await cms.article.publish("1");
  await cms.article.unpublish("1");
  await cms.article.delete("1");
  assert.deepEqual(sent.slice(1).map((request) => `${request.method} ${request.url}`), [
    "POST https://cms.example.com/api/management/v1/content/article/1/publish",
    "POST https://cms.example.com/api/management/v1/content/article/1/unpublish",
    "DELETE https://cms.example.com/api/management/v1/content/article/1",
  ]);
});

test("a problem of the API surfaces with its FCMS code", async () => {
  const { fetch } = answering(400, {
    type: "urn:fluentcms:error:FCMS2202",
    title: "Unknown field",
    status: 400,
    detail: "'nope' is not a field of article.",
    code: "FCMS2202",
    errors: [{ path: "filter[nope][eq]", code: "FCMS2202", message: "'nope' is not a field of article." }],
    traceId: "trace",
  }, "application/problem+json");
  const cms = createClient({ baseUrl: "https://cms.example.com/", fetch });

  await assert.rejects(cms.article.list(), (error) =>
    error instanceof FluentCmsProblem
    && error.status === 400
    && error.code === "FCMS2202"
    && error.errors[0]?.path === "filter[nope][eq]"
    && error.message.startsWith("FCMS2202: "));
});

test("a success that is not JSON is a problem with no code, a delete's too, and an empty delete answers nothing", async () => {
  const proxy = createClient({ baseUrl: "https://cms.example.com/", fetch: answering(200, "<html>a proxy</html>", "text/html").fetch });
  const empty = createClient({ baseUrl: "https://cms.example.com/", fetch: answering(200, "").fetch });

  await assert.rejects(proxy.author.get("1"), (error) =>
    error instanceof FluentCmsProblem
    && error.status === 200
    && error.code === undefined
    && error.detail === "The answer is not JSON."
    && error.message === "200: The answer is not JSON.");
  await assert.rejects(proxy.article.delete("1"), (error) => error instanceof FluentCmsProblem && error.detail === "The answer is not JSON.");
  assert.equal(await empty.article.delete("1"), undefined);
});

test("an answer that is not a problem has no code", async () => {
  const { fetch } = answering(502, "<html>gateway</html>", "text/html");
  const cms = createClient({ baseUrl: "https://cms.example.com/", fetch });

  await assert.rejects(cms.author.get("1"), (error) => error instanceof FluentCmsProblem && error.status === 502 && error.code === undefined);
});

test("an empty answer, a 204 or a null where a call returns a result is a problem; a delete returns none", async () => {
  for (const [status, answer, detail] of [[200, "", "The answer is empty."], [204, "", "The answer is empty."], [200, "null", "The answer is null."]] as const) {
    const client = new FluentCmsClient({ baseUrl: "https://cms.example.com/", fetch: answering(status, answer).fetch });
    const calls: (() => Promise<unknown>)[] = [
      () => client.list("article"),
      () => client.get("article", "1"),
      () => client.getBy("article", "slug", "hello"),
      () => client.getSingleton("settings"),
      () => client.create("article", { title: "Hello" }),
      () => client.update("article", "1", { title: "Hello" }, 1),
      () => client.publish("article", "1"),
      () => client.unpublish("article", "1"),
    ];
    for (const call of calls) {
      await assert.rejects(call, (error) => error instanceof FluentCmsProblem && error.status === status && error.code === undefined && error.detail === detail);
    }

    assert.equal(await client.delete("article", "1"), undefined);
  }
});

test(". and .. are refused in every segment, and any other value is sent escaped", async () => {
  const { fetch, sent } = answering(200, {});
  const client = new FluentCmsClient({ baseUrl: "https://cms.example.com/", fetch });

  for (const segment of [".", "..", ""]) {
    const calls: (() => Promise<unknown>)[] = [
      () => client.list(segment),
      () => client.get(segment, "1"),
      () => client.get("article", segment),
      () => client.getBy(segment, "slug", "hello"),
      () => client.getBy("article", segment, "hello"),
      () => client.getBy("article", "slug", segment),
      () => client.getSingleton(segment),
      () => client.create(segment, {}),
      () => client.update(segment, "1", {}, 1),
      () => client.update("article", segment, {}, 1),
      () => client.delete(segment, "1"),
      () => client.delete("article", segment),
      () => client.publish(segment, "1"),
      () => client.publish("article", segment),
      () => client.unpublish(segment, "1"),
      () => client.unpublish("article", segment),
    ];
    for (const call of calls) {
      await assert.rejects(call, (error) => error instanceof TypeError);
    }
  }

  assert.equal(sent.length, 0);
  for (const [value, path] of [["%2e%2e", "%252e%252e"], ["%2E", "%252E"], ["...", "..."], ["a/../b", "a%2F..%2Fb"]] as const) {
    await client.getBy("article", "slug", value);
    assert.equal(sent.at(-1)?.url, `https://cms.example.com/api/delivery/v1/article/by/slug/${path}`);
  }
});

test("a call is aborted by its signal and by timeoutMs", async () => {
  const fetch = (async (_input: string | URL | Request, init?: RequestInit): Promise<Response> =>
    await new Promise<Response>((_resolve, reject) => {
      const signal = init?.signal;
      if (signal?.aborted) {
        reject(signal.reason);
        return;
      }

      signal?.addEventListener("abort", () => reject(signal.reason), { once: true });
    })) as typeof globalThis.fetch;
  const client = new FluentCmsClient({ baseUrl: "https://cms.example.com/", fetch });
  const timed = new FluentCmsClient({ baseUrl: "https://cms.example.com/", fetch, timeoutMs: 20 });

  const controller = new AbortController();
  const listing = client.list("article", undefined, { signal: controller.signal });
  controller.abort(new Error("stopped"));
  await assert.rejects(listing, (error) => error instanceof Error && error.message === "stopped");
  await assert.rejects(timed.get("article", "1"), (error) => error instanceof DOMException && error.name === "TimeoutError");

  const early = new AbortController();
  early.abort(new Error("before"));
  await assert.rejects(timed.publish("article", "1", { signal: early.signal }), (error) => error instanceof Error && error.message === "before");
  const writing = new AbortController();
  const write = timed.create("article", { title: "Hello" }, { locale: "fr", signal: writing.signal });
  writing.abort(new Error("written"));
  await assert.rejects(write, (error) => error instanceof Error && error.message === "written");
  await assert.rejects(timed.delete("article", "1"), (error) => error instanceof DOMException && error.name === "TimeoutError");

  for (const timeoutMs of [0, -1, Number.NaN, Number.POSITIVE_INFINITY, 2_147_483_648]) {
    assert.throws(() => new FluentCmsClient({ baseUrl: "https://cms.example.com/", fetch, timeoutMs }), (error) => error instanceof RangeError);
  }
});

test("a problem has the Retry-After of the answer, in seconds", async () => {
  const problem = async (headers: Record<string, string>): Promise<FluentCmsProblem> => {
    const client = new FluentCmsClient({ baseUrl: "https://cms.example.com/", fetch: answering(429, { code: "FCMS3101" }, "application/problem+json", headers).fetch });
    try {
      await client.list("article");
    } catch (error) {
      if (error instanceof FluentCmsProblem) {
        return error;
      }
    }

    throw new Error("No problem was thrown.");
  };

  const limited = await problem({ "Retry-After": "30" });
  assert.equal(limited.retryAfter, 30);
  assert.equal(limited.code, "FCMS3101");
  assert.equal((await problem({ "Retry-After": "Thu, 19 Nov 2026 12:01:00 GMT", Date: "Thu, 19 Nov 2026 12:00:00 GMT" })).retryAfter, 60);
  assert.equal((await problem({ "Retry-After": "Thu, 19 Nov 2026 11:59:00 GMT", Date: "Thu, 19 Nov 2026 12:00:00 GMT" })).retryAfter, 0);
  assert.equal((await problem({ "Retry-After": "Thu, 19 Nov 2026 12:01:00 GMT" })).retryAfter, undefined);
  assert.equal((await problem({})).retryAfter, undefined);
  for (const malformed of ["-5", "1.5", "+30", "30s", "soon"]) {
    assert.equal((await problem({ "Retry-After": malformed, Date: "Thu, 19 Nov 2026 12:00:00 GMT" })).retryAfter, undefined, malformed);
  }
});
