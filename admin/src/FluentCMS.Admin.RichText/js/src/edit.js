// The edits of media and inline blocks (A-25), as transactions of an editor state, so that index.js applies them to the view
// and the schema's tests to a state alone. A node being edited is a target: where it was, its type, a block's id and a media
// node's asset, so that an edit never lands on another node that took its place.
import { NodeSelection, TextSelection } from "@tiptap/pm/state";
import { INLINE_BLOCK, INLINE_BLOCK_INLINE, MEDIA, MEDIA_INLINE, blockIds, isInlineBlock, isMedia, randomId, withFreshBlockIds } from "./schema.js";

function select(transaction, position) {
    return transaction.setSelection(NodeSelection.create(transaction.doc, position));
}

export function targetOf(position, node) {
    return { position, type: node.type.name, id: node.attrs.id ?? null, mediaId: node.attrs.mediaId ?? null };
}

// The media or inline block the selection is on.
export function selectedKept(state) {
    const { selection } = state;
    return selection instanceof NodeSelection && (isMedia(selection.node) || isInlineBlock(selection.node))
        ? { position: selection.from, node: selection.node }
        : null;
}

// The node of a target, if it is still where it was.
export function find(state, target) {
    const node = target ? state.doc.nodeAt(target.position) : null;
    return node && node.type.name === target.type && (node.attrs.id ?? null) === target.id && (node.attrs.mediaId ?? null) === target.mediaId
        ? { position: target.position, node }
        : null;
}

// A 12-character id that the document does not hold.
export function freshId(doc, random = randomId) {
    const taken = new Set(blockIds(doc));
    let id = random();
    while (taken.has(id)) {
        id = random();
    }

    return id;
}

// A media or an inline block goes in place of an empty paragraph at the selection, inline into the text of a paragraph or a
// heading, and after any other text block, such as code; it is selected once there.
export function insertKept(state, blockName, inlineName, attrs) {
    const { $from } = state.selection;
    const parent = $from.parent;
    const transaction = state.tr;
    let position;
    if (parent.type.name === "paragraph" && parent.content.size === 0 && $from.depth > 0) {
        position = $from.before();
        transaction.replaceWith(position, position + parent.nodeSize, state.schema.nodes[blockName].create(attrs));
    } else if (parent.type.name === "paragraph" || parent.type.name === "heading") {
        const node = state.schema.nodes[inlineName].create(attrs);
        transaction.replaceSelectionWith(node, false);
        position = transaction.selection.from - node.nodeSize;
    } else {
        position = $from.depth > 0 ? $from.after() : state.selection.to;
        transaction.insert(position, state.schema.nodes[blockName].create(attrs));
    }

    return { transaction: select(transaction, position).scrollIntoView(), position };
}

export function insertMedia(state, mediaId) {
    return insertKept(state, MEDIA, MEDIA_INLINE, { mediaId });
}

export function insertBlock(state, blockType, data, random = randomId) {
    return insertKept(state, INLINE_BLOCK, INLINE_BLOCK_INLINE, { blockType, id: freshId(state.doc, random), data });
}

// New attributes for the target; an inline block keeps its id.
export function updateKept(state, target, attrs) {
    const found = find(state, target);
    if (!found) {
        return null;
    }

    const next = { ...attrs };
    if (isInlineBlock(found.node)) {
        next.id = found.node.attrs.id;
    }

    return select(state.tr.setNodeMarkup(found.position, undefined, next), found.position);
}

export function removeKept(state, target) {
    const found = find(state, target);
    if (!found) {
        return null;
    }

    const transaction = state.tr.delete(found.position, found.position + found.node.nodeSize);
    return transaction.setSelection(TextSelection.near(transaction.doc.resolve(Math.min(found.position, transaction.doc.content.size))));
}

// A copy of the target inline block, after it, with a fresh id and fresh ids for the blocks its data holds (ADR 0002), by the
// schema's `fields` (withFreshBlockIds), so the references its data holds are kept.
export function duplicateKept(state, target, fields, random = randomId) {
    const found = find(state, target);
    if (!found || !isInlineBlock(found.node)) {
        return null;
    }

    const { node } = found;
    const data = node.attrs.data ? withFreshBlockIds(node.attrs.data, node.attrs.blockType, fields, random) : null;
    const copy = node.type.create({ ...node.attrs, id: freshId(state.doc, random), data });
    const position = found.position + node.nodeSize;
    return { transaction: select(state.tr.insert(position, copy), position).scrollIntoView(), position };
}
