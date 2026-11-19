// The edits of media and inline blocks (src/edit.js) on an editor state with undo and redo, as the editor applies them, and
// the ids of the blocks an inline block holds when it is duplicated or pasted as a copy (ADR 0002).
import assert from "node:assert/strict";
import { test } from "node:test";
import { getSchema } from "@tiptap/core";
import { history, redo, undo } from "@tiptap/pm/history";
import { EditorState, NodeSelection, TextSelection } from "@tiptap/pm/state";
import * as edits from "../src/edit.js";
import { extensions, fromEditor, toEditor, withFreshIds } from "../src/schema.js";

const schema = getSchema(extensions());
const Asset = "5c3a1e2f-9b8d-4c7e-a6f5-0d1b2c3e4f5a";
const Other = "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d";
const Tag = "0b9f4a52-7c1e-4d0a-9a43-5e1f6b2c8d70";

// The schema's fields that hold blocks, as the component gives them: a card holds links (blocks), a body (rich text) and an
// author and images that are references, which a copy keeps.
const fields = { blocks: { card: { links: "blocks", body: "richText", meta: "component:meta" }, link: {} }, components: { meta: { more: "blocks" } } };

const card = () => ({
    title: "Card",
    tags: [{ id: Tag, type: "tag" }],
    images: [{ id: Asset, type: "media" }],
    author: { id: Tag, type: "author" },
    links: [{ type: "link", id: "link0000001A", label: "a" }],
    body: { type: "doc", content: [{ type: "paragraph", content: [{ type: "inlineBlock", attrs: { blockType: "link", id: "deep00000001", data: {} } }] }] },
    meta: { more: [{ type: "link", id: "more00000001" }] },
});

function stateOf(document, selection) {
    let state = EditorState.create({ schema, doc: schema.nodeFromJSON(toEditor(document)), plugins: [history()] });
    if (selection) {
        state = state.apply(state.tr.setSelection(selection(state.doc)));
    }

    return state;
}

function apply(state, transaction) {
    return state.apply(transaction);
}

function run(state, command) {
    let next = state;
    command(state, (transaction) => {
        next = state.apply(transaction);
    });
    return next;
}

const json = (state) => fromEditor(state.doc.toJSON());

function counter(prefix = "fresh") {
    let next = 0;
    return () => `${prefix}${String(++next).padStart(12 - prefix.length, "0")}`;
}

test("media goes in place of an empty paragraph, inline into text, is selected, and undo and redo restore it", () => {
    const empty = stateOf({ type: "doc", content: [{ type: "paragraph" }] }, (doc) => TextSelection.create(doc, 1));
    const block = edits.insertMedia(empty, Asset);
    let state = apply(empty, block.transaction);
    assert.deepEqual(json(state).content, [{ type: "media", attrs: { mediaId: Asset } }]);
    assert.equal(state.selection instanceof NodeSelection && state.selection.from, block.position);

    const text = stateOf({ type: "doc", content: [{ type: "paragraph", content: [{ type: "text", text: "ab" }] }] }, (doc) => TextSelection.create(doc, 2));
    state = apply(text, edits.insertMedia(text, Asset).transaction);
    assert.deepEqual(json(state).content[0].content, [{ type: "text", text: "a" }, { type: "media", attrs: { mediaId: Asset } }, { type: "text", text: "b" }]);

    const undone = run(state, undo);
    assert.deepEqual(json(undone), json(text));
    assert.deepEqual(json(run(undone, redo)), json(state));
});

test("media after a code block goes after it, at block level", () => {
    const code = stateOf({ type: "doc", content: [{ type: "codeBlock", content: [{ type: "text", text: "x" }] }] }, (doc) => TextSelection.create(doc, 2));
    const state = apply(code, edits.insertMedia(code, Asset).transaction);
    assert.deepEqual(json(state).content.map((node) => node.type), ["codeBlock", "media"]);
});

test("a new inline block has a fresh id the document does not hold, kept through undo and redo", () => {
    const start = stateOf({ type: "doc", content: [{ type: "paragraph", content: [{ type: "inlineBlock", attrs: { blockType: "note", id: "fresh0000001" } }, { type: "text", text: "a" }] }] },
        (doc) => TextSelection.create(doc, 3));
    const inserted = edits.insertBlock(start, "note", { text: "" }, counter());
    const state = apply(start, inserted.transaction);
    const ids = () => json(state).content[0].content.filter((node) => node.type === "inlineBlock").map((node) => node.attrs.id);
    assert.deepEqual(ids(), ["fresh0000001", "fresh0000002"]);

    const redone = run(run(state, undo), redo);
    assert.deepEqual(json(redone), json(state));
});

test("an edit reaches its node only, keeps a block's id, and undo restores the attributes", () => {
    const document = { type: "doc", content: [
        { type: "media", attrs: { mediaId: Asset, alt: "Old" } },
        { type: "inlineBlock", attrs: { blockType: "note", id: "block0000001", data: { text: "One" } } },
    ] };
    let state = stateOf(document, (doc) => NodeSelection.create(doc, 0));
    const media = edits.targetOf(0, state.doc.nodeAt(0));
    state = apply(state, edits.updateKept(state, media, { mediaId: Asset, alt: "New" }));
    assert.deepEqual(json(state).content[0], { type: "media", attrs: { mediaId: Asset, alt: "New" } });

    const block = edits.targetOf(1, state.doc.nodeAt(1));
    state = apply(state, edits.updateKept(state, block, { blockType: "note", id: "other", data: { text: "Two" } }));
    assert.deepEqual(json(state).content[1], { type: "inlineBlock", attrs: { blockType: "note", id: "block0000001", data: { text: "Two" } } });
    assert.equal(state.selection.from, 1);

    assert.deepEqual(json(run(run(state, undo), undo)), document);
});

test("an edit whose node is no longer the one it was opened for changes nothing", () => {
    const state = stateOf({ type: "doc", content: [{ type: "media", attrs: { mediaId: Asset } }, { type: "paragraph" }] });
    const target = edits.targetOf(0, state.doc.nodeAt(0));
    const replaced = stateOf({ type: "doc", content: [{ type: "media", attrs: { mediaId: Other } }, { type: "paragraph" }] });

    assert.equal(edits.updateKept(replaced, target, { mediaId: Asset, alt: "x" }), null);
    assert.equal(edits.removeKept(replaced, target), null);
    assert.equal(edits.updateKept(state, null, { mediaId: Asset }), null);
});

test("a removed node comes back by undo", () => {
    const document = { type: "doc", content: [{ type: "paragraph", content: [{ type: "text", text: "a" }] }, { type: "media", attrs: { mediaId: Asset } }] };
    const start = stateOf(document);
    const state = apply(start, edits.removeKept(start, edits.targetOf(3, start.doc.nodeAt(3))));
    assert.deepEqual(json(state).content.map((node) => node.type), ["paragraph"]);
    assert.deepEqual(json(run(state, undo)), document);
});

test("a duplicate takes fresh ids for itself and its blocks and keeps the references its data holds", () => {
    const document = { type: "doc", content: [{ type: "inlineBlock", attrs: { blockType: "card", id: "card00000001", data: card() } }] };
    const start = stateOf(document);
    const duplicate = edits.duplicateKept(start, edits.targetOf(0, start.doc.nodeAt(0)), fields, counter());
    const state = apply(start, duplicate.transaction);
    const [original, copy] = json(state).content;

    assert.deepEqual(original, document.content[0]);
    assert.equal(copy.attrs.id, "fresh0000004");
    assert.deepEqual(copy.attrs.data.tags, [{ id: Tag, type: "tag" }]);
    assert.deepEqual(copy.attrs.data.images, [{ id: Asset, type: "media" }]);
    assert.deepEqual(copy.attrs.data.author, { id: Tag, type: "author" });
    assert.equal(copy.attrs.data.links[0].id, "fresh0000001");
    assert.equal(copy.attrs.data.body.content[0].content[0].attrs.id, "fresh0000002");
    assert.equal(copy.attrs.data.meta.more[0].id, "fresh0000003");
    assert.equal(state.selection.from, duplicate.position);

    assert.deepEqual(json(run(state, undo)), document);
    assert.deepEqual(json(run(run(state, undo), redo)), json(state));
});

test("a pasted copy keeps the references its data holds; without the schema's fields only its own id is fresh", () => {
    const slice = { content: [{ type: "inlineBlockInline", attrs: { blockType: "card", id: "card00000001", data: card() } }] };

    const pasted = withFreshIds(slice, ["card00000001"], counter(), fields).content[0].attrs;
    assert.equal(pasted.id, "fresh0000001");
    assert.deepEqual(pasted.data.tags, [{ id: Tag, type: "tag" }]);
    assert.deepEqual(pasted.data.images, [{ id: Asset, type: "media" }]);
    assert.equal(pasted.data.links[0].id, "fresh0000002");

    const unknown = withFreshIds(slice, ["card00000001"], counter()).content[0].attrs;
    assert.equal(unknown.id, "fresh0000001");
    assert.deepEqual(unknown.data, card());
});
