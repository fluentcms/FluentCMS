// The round trip set (admin spec 4.7 and 7): every document under documents/, which FluentCMS.Core.Tests also reads back
// through the server's parser and serializer, goes through the editor's schema unchanged.
import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { test } from "node:test";
import { getSchema } from "@tiptap/core";
import { EditorState } from "@tiptap/pm/state";
import {
    extensions, firstText, fits, fromEditor, isEmpty, isSafeHref, pathOf, references, toEditor, withFreshBlockIds, withFreshIds,
} from "../src/schema.js";

const schema = getSchema(extensions());
const folder = new URL("./documents/", import.meta.url);
const documents = readdirSync(folder).filter((name) => name.endsWith(".json"));

function read(name) {
    return JSON.parse(readFileSync(new URL(name, folder), "utf8"));
}

function load(document) {
    const node = schema.nodeFromJSON(toEditor(document));
    node.check();
    return node;
}

test("the set has a document for each kind of content", () => {
    assert.deepEqual(documents.sort(), ["blocks.json", "kept.json", "lists.json", "marks.json", "structures.json"]);
});

for (const name of documents) {
    test(`${name} goes through the editor's schema unchanged`, () => {
        const document = read(name);
        assert.equal(fits(schema, document), true);
        assert.deepEqual(fromEditor(load(document).toJSON()), document);
    });
}

test("every node and mark of the server's model is in the set", () => {
    const nodes = new Set();
    const marks = new Set();
    const visit = (node) => {
        nodes.add(node.type);
        node.marks?.forEach((mark) => marks.add(mark.type));
        node.content?.forEach(visit);
    };
    documents.map(read).forEach(visit);

    assert.deepEqual([...nodes].sort(), ["blockquote", "bulletList", "codeBlock", "doc", "hardBreak", "heading", "horizontalRule",
        "inlineBlock", "listItem", "media", "orderedList", "paragraph", "text"]);
    assert.deepEqual([...marks].sort(), ["bold", "code", "entryLink", "italic", "link", "strike", "underline"]);
});

test("kept nodes and marks stay unchanged when the text around them is edited", () => {
    const document = read("kept.json");
    let state = EditorState.create({ schema, doc: load(document) });

    // Type at the start of the first paragraph, and remove the word "See".
    state = state.apply(state.tr.insertText("Now ", 1));
    state = state.apply(state.tr.delete(5, 9));
    state = state.apply(state.tr.insertText("!", state.doc.child(0).nodeSize - 1));

    const edited = fromEditor(state.doc.toJSON());
    const kept = (doc) => doc.content.flatMap((block) => [block, ...(block.content ?? [])])
        .filter((node) => node.type === "media" || node.type === "inlineBlock" || node.marks?.some((mark) => mark.type === "entryLink"));
    assert.deepEqual(kept(edited), kept(document));
    assert.equal(edited.content[0].content[0].text, "Now ");
    assert.equal(edited.content[0].content.at(-1).text, ".!");
});

test("a link's href is a relative link or one of http, https, mailto and tel", () => {
    for (const href of ["https://example.com", "http://example.com/a", "HTTPS://EXAMPLE.COM", "mailto:a@example.com", "tel:+1555",
        "/relative", "relative/path", "./here", "../up", "#anchor", "?query", "//example.com/path", "a&1/c", "/a?b=c&d=e:f"]) {
        assert.equal(isSafeHref(href), true, href);
    }

    for (const href of ["javascript:alert(1)", " JavaScript:alert(1)", "java\tscript:alert(1)", "data:text/html,x", "vbscript:x",
        "ftp://example.com", "javascript&#58;alert(1)", "javascript&colon;alert(1)", "", "   ", null]) {
        assert.equal(isSafeHref(href), false, String(href));
    }
});

test("an empty document is one empty paragraph or no content", () => {
    assert.equal(isEmpty({ type: "doc" }), true);
    assert.equal(isEmpty({ type: "doc", content: [{ type: "paragraph" }] }), true);
    assert.equal(isEmpty({ type: "doc", content: [{ type: "paragraph", content: [{ type: "text", text: "a" }] }] }), false);
    assert.equal(isEmpty({ type: "doc", content: [{ type: "horizontalRule" }] }), false);
});

// content.json is the server's NodeSpec, which FluentCMS.Core.Tests holds it to; the schema is held to it here, so a change of
// the model fails one of the two.
test("each node's content and place are the server's", () => {
    const contract = JSON.parse(readFileSync(new URL("./content.json", import.meta.url), "utf8"));
    const expressions = { Blocks: "block*", Inlines: "inline*", ListItems: "listItem*", Text: "text*", None: undefined };
    const groups = { Block: ["block"], Inline: ["inline"], BlockOrInline: ["block", "inline"], ListItem: [undefined], Root: [undefined] };
    const names = [];
    for (const [name, spec] of Object.entries(contract)) {
        const variants = spec.position === "BlockOrInline" ? [name, `${name}Inline`] : [name];
        variants.forEach((variant, index) => {
            const type = schema.nodes[variant];
            assert.ok(type, variant);
            names.push(variant);
            // The document holds at least one block, so that there is somewhere to type; toEditor fills an empty one.
            const expected = name === "doc" ? "block+" : expressions[spec.content];
            assert.equal(type.spec.content || undefined, expected, variant);
            const group = (type.spec.group ?? "").split(" ");
            const place = groups[spec.position][index];
            assert.equal(group.includes("block"), place === "block", variant);
            assert.equal(group.includes("inline"), place === "inline", variant);
            if (spec.content === "Inlines") {
                assert.ok(type.spec.marks === undefined || type.spec.marks === "_", variant);
            } else if (spec.content === "Text") {
                assert.equal(type.spec.marks, "", variant);
            }
        });
    }

    assert.deepEqual(Object.keys(schema.nodes).sort(), names.sort());
    assert.deepEqual(Object.keys(schema.marks).sort(), ["bold", "code", "entryLink", "italic", "link", "strike", "underline"]);
});

test("a document the editor cannot hold whole does not fit", () => {
    const paragraph = (extra) => ({ type: "doc", content: [{ type: "paragraph", ...extra, content: [{ type: "text", text: "a" }] }] });
    assert.equal(fits(schema, paragraph({})), true);
    assert.equal(fits(schema, paragraph({ attrs: { textAlign: "center" } })), false);
    assert.equal(fits(schema, { type: "doc", content: [{ type: "heading", attrs: { level: 2, id: "intro" } }] }), false);
    assert.equal(fits(schema, { type: "doc", content: [{ type: "table" }] }), false);
    assert.equal(fits(schema, { type: "doc", content: [{ type: "paragraph", content: [{ type: "text", text: "a", marks: [{ type: "highlight" }] }] }] }), false);
    assert.equal(fits(schema, { type: "doc", content: [{ type: "paragraph", content: [{ type: "text", text: "a", marks: [{ type: "link", attrs: { href: "/", rel: "x" } }] }] }] }), false);
    assert.equal(fits(schema, { type: "doc", content: [{ type: "text", text: "loose text" }] }), false);
    assert.equal(fits(schema, { type: "paragraph" }), false);
    assert.equal(fits(schema, null), false);
    assert.equal(fits(schema, []), false);
});

test("an empty document gets a paragraph to type in", () => {
    assert.equal(fits(schema, { type: "doc" }), true);
    assert.equal(isEmpty(fromEditor(load({ type: "doc" }).toJSON())), true);
});

test("the editor orders marks by its schema, which is no change of the document", () => {
    const document = { type: "doc", content: [{ type: "paragraph", content: [{ type: "text", text: "a", marks: [{ type: "bold" }, { type: "link", attrs: { href: "/" } }] }] }] };
    assert.deepEqual(fromEditor(load(document).toJSON()).content[0].content[0].marks.map((mark) => mark.type), ["link", "bold"]);
});

test("a pasted inline block takes a fresh id only when its id is taken", () => {
    const block = (id) => ({ type: "inlineBlockInline", attrs: { blockType: "note", id } });
    const slice = { content: [{ type: "paragraph", content: [block("kept"), block("taken"), block("kept")] }], openStart: 1, openEnd: 1 };
    let next = 0;
    const pasted = withFreshIds(slice, ["taken"], () => `fresh${++next}`).content[0].content.map((node) => node.attrs.id);
    assert.deepEqual(pasted, ["kept", "fresh1", "fresh2"]);
    assert.equal(slice.content[0].content[1].attrs.id, "taken");
});

test("a fresh id has 12 URL-safe characters", async () => {
    const { randomId } = await import("../src/schema.js");
    const id = randomId();
    assert.match(id, /^[A-Za-z0-9_-]{12}$/);
});

test("a document of entry links, media and inline blocks fits, and one with anything else beside them does not", () => {
    const kept = read("kept.json");
    assert.equal(fits(schema, kept), true);
    assert.equal(fits(schema, { ...kept, content: [...kept.content, { type: "video", attrs: { src: "a.mp4" } }] }), false);
    const withUnknownAttribute = structuredClone(kept);
    withUnknownAttribute.content[1].attrs.url = "https://example.com/a.png";
    assert.equal(fits(schema, withUnknownAttribute), false);
    const withUnknownMark = structuredClone(kept);
    withUnknownMark.content[0].content[1].marks.push({ type: "comment", attrs: { id: "1" } });
    assert.equal(fits(schema, withUnknownMark), false);
});

test("a pasted copy of an inline block takes fresh ids for the blocks its data holds too", () => {
    const data = { title: "Card", items: [{ type: "link", id: "inner", label: "a" }], body: { type: "doc", content: [{ type: "inlineBlock", attrs: { blockType: "note", id: "deep" } }] } };
    const slice = { content: [{ type: "inlineBlock", attrs: { blockType: "card", id: "taken", data } }] };
    let next = 0;
    const pasted = withFreshIds(slice, ["taken"], () => `fresh${++next}`).content[0].attrs;
    assert.equal(pasted.id, "fresh1");
    assert.equal(pasted.data.items[0].id, "fresh2");
    assert.equal(pasted.data.body.content[0].attrs.id, "fresh3");
    assert.equal(pasted.data.title, "Card");
    assert.equal(data.items[0].id, "inner");

    const moved = withFreshIds(slice, [], () => "never").content[0].attrs;
    assert.deepEqual(moved, slice.content[0].attrs);
});

test("a duplicate's data takes fresh ids for its blocks only", () => {
    let next = 0;
    const fresh = withFreshBlockIds({ id: "own", tags: [{ id: "x" }], blocks: [{ type: "quote", id: "q" }] }, () => `n${++next}`);
    assert.deepEqual(fresh, { id: "own", tags: [{ id: "x" }], blocks: [{ type: "quote", id: "n1" }] });
});

test("the references of a document are its media and its linked entries, each once", () => {
    const document = load(read("kept.json"));
    const found = references(document);
    assert.deepEqual(found.media, ["0f1e2d3c-4b5a-4968-8776-655443322110", "5c3a1e2f-9b8d-4c7e-a6f5-0d1b2c3e4f5a", "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d"]);
    assert.deepEqual(found.entries, [
        { id: "0b9f4a52-7c1e-4d0a-9a43-5e1f6b2c8d70", type: "article" },
        { id: "7d2e6c11-3a5b-4f80-b9c4-2a6d1e8f0b35", type: "author" },
    ]);
});

test("a node's path is the one the server's errors name", () => {
    const document = load(read("kept.json"));
    const positions = [];
    document.descendants((node, position) => {
        if (node.type.name.startsWith("inlineBlock")) {
            positions.push(position);
        }
    });
    assert.deepEqual(positions.map((position) => pathOf(document, position)), [".content[0].content[7]", ".content[3]", ".content[4]"]);
});

test("a block's preview is the first text of its data on one line", () => {
    assert.equal(firstText({ level: 2, title: "  Two\n lines " }), "Two lines");
    assert.equal(firstText({ level: 2 }), "");
    assert.equal(firstText(null), "");
    assert.equal(firstText({ text: "x".repeat(80) }).length, 60);
});
