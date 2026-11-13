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

function answering(status: number, body: unknown, contentType = "application/json"): { fetch: typeof fetch; sent: Sent[] } {
  const sent: Sent[] = [];
  const fake = async (input: string | URL | Request, init?: RequestInit): Promise<Response> => {
    sent.push({
      url: String(input),
      method: init?.method ?? "GET",
      headers: (init?.headers ?? {}) as Record<string, string>,
      body: typeof init?.body === "string" ? init.body : undefined,
    });
    return new Response(typeof body === "string" ? body : JSON.stringify(body), { status, headers: { "Content-Type": contentType } });
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

test("an answer that is not a problem has no code", async () => {
  const { fetch } = answering(502, "<html>gateway</html>", "text/html");
  const cms = createClient({ baseUrl: "https://cms.example.com/", fetch });

  await assert.rejects(cms.author.get("1"), (error) => error instanceof FluentCmsProblem && error.status === 502 && error.code === undefined);
});
