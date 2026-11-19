// The round trip set (admin spec 4.7 and 7): every document under documents/, which FluentCMS.Core.Tests also reads back
// through the server's parser and serializer, goes through the editor's schema unchanged.
import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { test } from "node:test";
import { getSchema } from "@tiptap/core";
import { EditorState } from "@tiptap/pm/state";
import { extensions, fromEditor, isEmpty, isSafeHref, toEditor } from "../src/schema.js";

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
    assert.deepEqual(documents.sort(), ["blocks.json", "kept.json", "lists.json", "marks.json"]);
});

for (const name of documents) {
    test(`${name} goes through the editor's schema unchanged`, () => {
        const document = read(name);
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
