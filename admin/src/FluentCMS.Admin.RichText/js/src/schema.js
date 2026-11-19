// The editor's schema is the server's rich text model (backend spec 4.2, src/FluentCMS.Core/RichText): the same node and
// mark names and attributes. Links to entries, media and inline blocks hold ids only, never a URL; what the editor shows of
// them (an entry's title, a media preview, a block's type) comes from Blazor through `views` and is never part of the
// document. The server places media and inline blocks at block level or inline, and a ProseMirror node is one or the other,
// so the inline ones take a name of their own in the editor, which toEditor and fromEditor translate.
import { Mark, Node } from "@tiptap/core";
import { Plugin, PluginKey } from "@tiptap/pm/state";
import { Decoration, DecorationSet } from "@tiptap/pm/view";
import Blockquote from "@tiptap/extension-blockquote";
import Bold from "@tiptap/extension-bold";
import Code from "@tiptap/extension-code";
import CodeBlock from "@tiptap/extension-code-block";
import Document from "@tiptap/extension-document";
import HardBreak from "@tiptap/extension-hard-break";
import Heading from "@tiptap/extension-heading";
import HorizontalRule from "@tiptap/extension-horizontal-rule";
import Italic from "@tiptap/extension-italic";
import Link from "@tiptap/extension-link";
import { BulletList, ListItem, ListKeymap, OrderedList } from "@tiptap/extension-list";
import Paragraph from "@tiptap/extension-paragraph";
import Strike from "@tiptap/extension-strike";
import Text from "@tiptap/extension-text";
import Underline from "@tiptap/extension-underline";

export const MEDIA = "media";
export const INLINE_BLOCK = "inlineBlock";
export const MEDIA_INLINE = "mediaInline";
export const INLINE_BLOCK_INLINE = "inlineBlockInline";
export const ENTRY_LINK = "entryLink";

export function isMedia(node) {
    return node?.type.name === MEDIA || node?.type.name === MEDIA_INLINE;
}

export function isInlineBlock(node) {
    return node?.type.name === INLINE_BLOCK || node?.type.name === INLINE_BLOCK_INLINE;
}

// The nodes whose content is inline in the server's model; a media or inline block inside one is inline.
const INLINE_PARENTS = new Set(["paragraph", "heading"]);

const ALLOWED_SCHEMES = ["http", "https", "mailto", "tel"];

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// The server's rule for a link (SafeUrl of FormatRenderer.cs): a relative link, or one of http, https, mailto and tel. A
// character reference before the scheme's end is refused, as it can spell the colon.
export function isSafeHref(href) {
    if (typeof href !== "string") {
        return false;
    }

    const trimmed = href.trim();
    if (trimmed.length === 0 || /[\u0000-\u001f\u007f-\u009f]/.test(trimmed)) {
        return false;
    }

    let end = -1;
    for (let index = 0; index < trimmed.length && end < 0; index++) {
        const character = trimmed[index];
        if (character === "/" || character === "?" || character === ":" || (character === "#" && trimmed[index - 1] !== "&")) {
            end = index;
        }
    }

    const prefix = end < 0 ? trimmed : trimmed.slice(0, end);
    if (/&[#A-Za-z]/.test(prefix)) {
        return false;
    }

    return end < 0 || trimmed[end] !== ":" || ALLOWED_SCHEMES.includes(prefix.toLowerCase());
}

// Kept nodes and marks travel through the clipboard as elements marked with the origin of the editor that wrote them: a
// slice of the same editor keeps them whole, and HTML from anywhere else never makes them, since an id there is unchecked.
const ORIGIN = "data-fluentcms-origin";

function fromOrigin(element, origin) {
    return origin !== undefined && element.getAttribute(ORIGIN) === origin;
}

// An attribute kept as a data attribute of the element, so that a copy and paste inside the editor keeps it.
function optional(name, dataName) {
    return {
        default: null,
        parseHTML: (element) => element.getAttribute(`data-${dataName}`),
        renderHTML: (attributes) => (attributes[name] == null ? {} : { [`data-${dataName}`]: attributes[name] }),
    };
}

// What the editor shows of a kept node: its kind and a name, never its data, which may be long.
function keptLabel(labels, kind, name) {
    const text = labels?.[kind] ?? kind;
    return name ? `${text}: ${name}` : text;
}

// A preview is shown only from an http, https or relative address, so nothing else reaches an img.
function isPreviewUrl(url) {
    return typeof url === "string" && isSafeHref(url) && !/^(mailto|tel):/i.test(url.trim());
}

// A media node as the editor shows it: the asset's preview, read by Blazor from the Management API, or a placeholder when the
// media plugin is not enabled in the site or the asset is gone. The view is named for assistive technology by the node's
// alternative text, the asset's, or its file name.
function mediaView(inline, views) {
    return ({ node }) => {
        let current = node;
        const dom = document.createElement(inline ? "span" : "div");
        dom.className = inline ? "rich-text-media rich-text-media-inline" : "rich-text-media";
        dom.contentEditable = "false";
        dom.setAttribute("role", "img");
        dom.setAttribute("data-fluentcms-media", "");
        const image = document.createElement("img");
        image.alt = "";
        image.draggable = false;
        const caption = document.createElement("span");
        caption.className = "rich-text-media-caption";
        caption.setAttribute("aria-hidden", "true");
        dom.append(caption);

        const render = () => {
            const info = views.media(current.attrs.mediaId) ?? { state: "loading" };
            const labels = views.labels;
            let text = current.attrs.alt ?? info.alt ?? current.attrs.title ?? info.name ?? "";
            if (info.state === "unavailable") {
                text = labels.mediaUnavailable;
            } else if (info.state === "missing") {
                text = labels.mediaMissing;
            }

            const source = info.state === "ready" && isPreviewUrl(info.preview) ? info.preview : null;
            if (source) {
                if (image.getAttribute("src") !== source) {
                    image.setAttribute("src", source);
                }

                if (!image.isConnected) {
                    dom.prepend(image);
                }
            } else {
                image.remove();
                image.removeAttribute("src");
            }

            dom.classList.toggle("rich-text-media-placeholder", !source);
            caption.textContent = text || labels.media;
            dom.setAttribute("aria-label", keptLabel(labels, MEDIA, text));
        };

        const view = { id: () => current.attrs.mediaId, render };
        views.attach(view);
        render();
        return {
            dom,
            update: (next) => {
                if (next.type !== current.type) {
                    return false;
                }

                current = next;
                render();
                return true;
            },
            ignoreMutation: () => true,
            destroy: () => views.detach(view),
        };
    };
}

// An inline block as the editor shows it: its type's name and the first text of its data, which Enter opens in its form.
function inlineBlockView(inline, views) {
    return ({ node }) => {
        let current = node;
        const dom = document.createElement(inline ? "span" : "div");
        dom.className = "rich-text-kept rich-text-block";
        dom.contentEditable = "false";
        dom.setAttribute("role", "group");
        dom.setAttribute("data-fluentcms-block", "");

        const render = () => {
            const type = views.blockLabel(current.attrs.blockType) ?? current.attrs.blockType ?? "";
            const preview = firstText(current.attrs.data);
            const text = keptLabel(views.labels, INLINE_BLOCK, preview ? `${type}: ${preview}` : type);
            dom.textContent = text;
            dom.setAttribute("aria-label", text);
            if (current.attrs.id) {
                dom.setAttribute("data-block-id", current.attrs.id);
            } else {
                dom.removeAttribute("data-block-id");
            }
        };

        const view = { id: () => null, render };
        views.attach(view);
        render();
        return {
            dom,
            update: (next) => {
                if (next.type !== current.type) {
                    return false;
                }

                current = next;
                render();
                return true;
            },
            ignoreMutation: () => true,
            destroy: () => views.detach(view),
        };
    };
}

// The first text of a block's data on one line, at most 60 characters, as A-26 previews a collapsed block.
export function firstText(data) {
    if (data === null || typeof data !== "object" || Array.isArray(data)) {
        return "";
    }

    for (const value of Object.values(data)) {
        if (typeof value === "string" && value.trim().length > 0) {
            const line = value.trim().replace(/\s+/g, " ");
            return line.length > 60 ? `${line.slice(0, 59)}…` : line;
        }
    }

    return "";
}

function media(name, inline, labels, origin, views) {
    return Node.create({
        name,
        group: inline ? "inline" : "block",
        inline,
        atom: true,
        selectable: true,
        draggable: true,
        addAttributes() {
            return { mediaId: optional("mediaId", "media-id"), alt: optional("alt", "alt"), title: optional("title", "title") };
        },
        parseHTML() {
            return [{
                tag: `${inline ? "span" : "div"}[data-fluentcms-media]`,
                getAttrs: (element) => (fromOrigin(element, origin) && GUID.test(element.getAttribute("data-media-id") ?? "") ? null : false),
            }];
        },
        renderHTML({ HTMLAttributes, node }) {
            return [inline ? "span" : "div", { ...HTMLAttributes, "data-fluentcms-media": "", [ORIGIN]: origin, class: "rich-text-kept", contenteditable: "false" },
                keptLabel(labels, MEDIA, node.attrs.alt ?? node.attrs.title)];
        },
        renderText: () => "",
        ...(views ? { addNodeView: () => mediaView(inline, views) } : {}),
    });
}

function inlineBlock(name, inline, labels, origin, views) {
    return Node.create({
        name,
        group: inline ? "inline" : "block",
        inline,
        atom: true,
        selectable: true,
        draggable: true,
        addAttributes() {
            return {
                blockType: optional("blockType", "block-type"),
                id: optional("id", "block-id"),
                data: {
                    default: null,
                    parseHTML: (element) => {
                        try {
                            const data = JSON.parse(element.getAttribute("data-block-data") ?? "null");
                            return data !== null && typeof data === "object" && !Array.isArray(data) ? data : null;
                        } catch {
                            return null;
                        }
                    },
                    renderHTML: (attributes) => (attributes.data == null ? {} : { "data-block-data": JSON.stringify(attributes.data) }),
                },
            };
        },
        parseHTML() {
            return [{
                tag: `${inline ? "span" : "div"}[data-fluentcms-block]`,
                getAttrs: (element) => (fromOrigin(element, origin) && /^[A-Za-z][A-Za-z0-9_-]*$/.test(element.getAttribute("data-block-type") ?? "") ? null : false),
            }];
        },
        renderHTML({ HTMLAttributes, node }) {
            return [inline ? "span" : "div", { ...HTMLAttributes, "data-fluentcms-block": "", [ORIGIN]: origin, class: "rich-text-kept", contenteditable: "false" },
                keptLabel(labels, INLINE_BLOCK, node.attrs.blockType)];
        },
        renderText: () => "",
        ...(views ? { addNodeView: () => inlineBlockView(inline, views) } : {}),
    });
}

export const entryTitles = new PluginKey("fluentcmsEntryTitles");

// Each entry link names its entry by a title of the text, which a decoration gives, so the document holds the id alone.
function entryTitlesPlugin(views) {
    const build = (doc) => {
        const decorations = [];
        doc.descendants((node, position) => {
            const mark = node.isText ? node.marks.find((item) => item.type.name === ENTRY_LINK) : null;
            if (mark) {
                const title = views.entryTitle(mark.attrs.entryId, mark.attrs.entryType);
                if (title) {
                    decorations.push(Decoration.inline(position, position + node.nodeSize, { title }));
                }
            }
        });
        return DecorationSet.create(doc, decorations);
    };

    return new Plugin({
        key: entryTitles,
        state: {
            init: (_, state) => build(state.doc),
            apply: (transaction, old) => (transaction.docChanged || transaction.getMeta(entryTitles) ? build(transaction.doc) : old),
        },
        props: { decorations: (state) => entryTitles.getState(state) },
    });
}

function entryLink(origin, views) {
    return Mark.create({
        name: ENTRY_LINK,
        inclusive: false,
        addAttributes() {
            return { entryId: optional("entryId", "entry-id"), entryType: optional("entryType", "entry-type") };
        },
        parseHTML() {
            return [{
                tag: "span[data-entry-id]",
                getAttrs: (element) => (fromOrigin(element, origin) && GUID.test(element.getAttribute("data-entry-id") ?? "") ? null : false),
            }];
        },
        renderHTML({ HTMLAttributes }) {
            return ["span", { ...HTMLAttributes, [ORIGIN]: origin, class: "rich-text-entry-link" }, 0];
        },
        ...(views ? { addProseMirrorPlugins: () => [entryTitlesPlugin(views)] } : {}),
    });
}

// Every extension of the editor; `labels` names the kept nodes in the interface's language, `origin` marks what this editor
// puts on the clipboard, and `views`, when given, is what the node views and the entry titles show (index.js); it changes
// nothing of the schema. The content of each node is the server's (NodeSpec.cs, test/content.json) but the document's, which
// holds at least one block so that there is somewhere to type; toEditor gives an empty document its paragraph.
export function extensions(labels, origin, views) {
    return [
        Document,
        Paragraph,
        Text,
        Heading.configure({ levels: [1, 2, 3, 4, 5, 6] }),
        Blockquote.extend({ content: "block*" }),
        // The server keeps a code block's language when it has one, and no attribute when it has none.
        CodeBlock.extend({
            addAttributes() {
                return {
                    language: {
                        default: null,
                        parseHTML: (element) => {
                            const name = [...(element.firstElementChild?.classList ?? [])].find((item) => item.startsWith("language-"));
                            return name ? name.slice("language-".length) : null;
                        },
                        rendered: false,
                    },
                };
            },
        }).configure({ enableTabIndentation: false }),
        HorizontalRule.extend({
            addKeyboardShortcuts() {
                return { "Mod-Alt-r": () => this.editor.commands.setHorizontalRule() };
            },
        }),
        HardBreak,
        BulletList.extend({ content: "listItem*" }),
        // A list without a start has none in the server's model, so the editor adds none either.
        OrderedList.extend({
            content: "listItem*",
            addAttributes() {
                return {
                    start: {
                        default: null,
                        parseHTML: (element) => (element.hasAttribute("start") ? parseInt(element.getAttribute("start") ?? "", 10) : null),
                        renderHTML: (attributes) => (attributes.start == null ? {} : { start: attributes.start }),
                    },
                };
            },
        }),
        ListItem.extend({ content: "block*" }),
        ListKeymap,
        Bold,
        Italic,
        Underline,
        Strike,
        // The server lets code text carry other marks, so code excludes only itself here.
        Code.extend({ excludes: "code" }),
        Link.extend({
            addAttributes() {
                return {
                    href: { default: null, parseHTML: (element) => element.getAttribute("href") },
                    title: { default: null, parseHTML: (element) => element.getAttribute("title") },
                    target: { default: null, parseHTML: (element) => element.getAttribute("target") },
                };
            },
        }).configure({
            openOnClick: false,
            autolink: true,
            linkOnPaste: true,
            defaultProtocol: "https",
            isAllowedUri: (url) => isSafeHref(url),
            shouldAutoLink: (url) => isSafeHref(url),
            HTMLAttributes: { rel: "noopener noreferrer nofollow", target: null, class: null },
        }),
        entryLink(origin, views),
        media(MEDIA, false, labels, origin, views),
        media(MEDIA_INLINE, true, labels, origin, views),
        inlineBlock(INLINE_BLOCK, false, labels, origin, views),
        inlineBlock(INLINE_BLOCK_INLINE, true, labels, origin, views),
    ];
}

// A document of the server's model as the editor's JSON.
export function toEditor(document) {
    const renamed = rename(document, false);
    return renamed?.type === "doc" && !(renamed.content?.length > 0) ? { ...renamed, content: [{ type: "paragraph" }] } : renamed;
}

// Whether the editor can hold a document of the server's model without losing anything: every node and mark is one the
// schema declares, with no attribute it does not declare, and the document passes the schema's check. One that does not fit
// is shown read-only, as JSON, and never written back.
export function fits(schema, document) {
    if (document === null || typeof document !== "object" || Array.isArray(document) || document.type !== "doc") {
        return false;
    }

    const declared = (type, name) => Object.hasOwn(type.spec.attrs ?? {}, name);
    const known = (node) => {
        const type = node && schema.nodes[node.type];
        if (!type || (node.attrs && Object.keys(node.attrs).some((name) => !declared(type, name)))) {
            return false;
        }

        for (const mark of node.marks ?? []) {
            const markType = mark && schema.marks[mark.type];
            if (!markType || (mark.attrs && Object.keys(mark.attrs).some((name) => !declared(markType, name)))) {
                return false;
            }
        }

        return (node.content ?? []).every(known);
    };

    const converted = toEditor(document);
    if (!known(converted)) {
        return false;
    }

    try {
        schema.nodeFromJSON(converted).check();
        return true;
    } catch {
        return false;
    }
}

// A slice pasted into the editor, as JSON: each inline block whose id the document or the slice already holds takes a fresh
// one, and so do the blocks its data holds, so a copy pasted beside its original is a new block; a block moved by cut and
// paste keeps its ids (ADR 0002).
export function withFreshIds(slice, taken, random = randomId) {
    const seen = new Set(taken);
    const visit = (node) => {
        if (node === null || typeof node !== "object") {
            return node;
        }

        const copy = { ...node };
        if ((copy.type === INLINE_BLOCK || copy.type === INLINE_BLOCK_INLINE) && copy.attrs?.id) {
            let id = copy.attrs.id;
            if (seen.has(id)) {
                while (seen.has(id)) {
                    id = random();
                }

                copy.attrs = { ...copy.attrs, id, ...(copy.attrs.data ? { data: withFreshBlockIds(copy.attrs.data, random) } : {}) };
            }

            seen.add(id);
        }

        if (Array.isArray(copy.content)) {
            copy.content = copy.content.map(visit);
        }

        return copy;
    };

    return { ...slice, content: (slice.content ?? []).map(visit) };
}

// A copy of a block's data whose blocks take fresh ids, at any depth: an item of a blocks field (an object with a type and an
// id in a list) and an inline block of a rich text field, as A-26 does for a duplicated block.
export function withFreshBlockIds(data, random = randomId) {
    const visit = (value, inList) => {
        if (Array.isArray(value)) {
            return value.map((item) => visit(item, true));
        }

        if (value === null || typeof value !== "object") {
            return value;
        }

        const copy = Object.fromEntries(Object.entries(value).map(([name, item]) => [name, visit(item, false)]));
        if (inList && typeof copy.type === "string" && typeof copy.id === "string") {
            copy.id = random();
        }

        if (copy.type === INLINE_BLOCK && copy.attrs !== null && typeof copy.attrs === "object" && typeof copy.attrs.id === "string") {
            copy.attrs = { ...copy.attrs, id: random() };
        }

        return copy;
    };

    return visit(data, false);
}

// What a document refers to, which Blazor reads to show: the ids of its media and its linked entries, in a stable order.
export function references(document) {
    const media = new Set();
    const entries = new Map();
    document.descendants((node) => {
        if (isMedia(node) && node.attrs.mediaId) {
            media.add(node.attrs.mediaId);
        }

        for (const mark of node.marks) {
            if (mark.type.name === ENTRY_LINK && mark.attrs.entryId && mark.attrs.entryType) {
                entries.set(`${mark.attrs.entryType}/${mark.attrs.entryId}`, { id: mark.attrs.entryId, type: mark.attrs.entryType });
            }
        }
    });
    const key = (entry) => `${entry.type}/${entry.id}`;
    return { media: [...media].sort(), entries: [...entries.values()].sort((a, b) => (key(a) < key(b) ? -1 : 1)) };
}

// Where the node at `position` is in the server's document, as its error paths name it, such as ".content[2].content[0]".
export function pathOf(document, position) {
    const resolved = document.resolve(position);
    let path = "";
    for (let depth = 0; depth <= resolved.depth; depth++) {
        path += `.content[${resolved.index(depth)}]`;
    }

    return path;
}

const ID_ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-";

// A 12-character block id, as the admin makes for a new block (spec 4.8).
export function randomId() {
    const bytes = crypto.getRandomValues(new Uint8Array(12));
    return Array.from(bytes, (byte) => ID_ALPHABET[byte & 63]).join("");
}

// The ids of the inline blocks of the editor's document.
export function blockIds(document) {
    const ids = [];
    document.descendants((node) => {
        if ((node.type.name === INLINE_BLOCK || node.type.name === INLINE_BLOCK_INLINE) && node.attrs.id) {
            ids.push(node.attrs.id);
        }
    });
    return ids;
}

// The editor's JSON as a document of the server's model: inline media and blocks take their names back, and an attribute
// the editor holds as null, which the server's model leaves out, is left out.
export function fromEditor(document) {
    return rename(document, false, true);
}

function rename(node, inline, fromTheEditor = false) {
    if (node === null || typeof node !== "object" || Array.isArray(node)) {
        return node;
    }

    const copy = { ...node };
    if (fromTheEditor) {
        if (copy.type === MEDIA_INLINE) {
            copy.type = MEDIA;
        } else if (copy.type === INLINE_BLOCK_INLINE) {
            copy.type = INLINE_BLOCK;
        }

        if (copy.attrs) {
            copy.attrs = withoutNulls(copy.attrs);
        }

        if (copy.marks) {
            copy.marks = copy.marks.map((mark) => (mark.attrs ? { ...mark, attrs: withoutNulls(mark.attrs) } : mark))
                .map((mark) => (mark.attrs && Object.keys(mark.attrs).length === 0 ? withoutAttrs(mark) : mark));
        }

        if (copy.attrs && Object.keys(copy.attrs).length === 0) {
            delete copy.attrs;
        }
    } else if (inline && copy.type === MEDIA) {
        copy.type = MEDIA_INLINE;
    } else if (inline && copy.type === INLINE_BLOCK) {
        copy.type = INLINE_BLOCK_INLINE;
    }

    if (Array.isArray(copy.content)) {
        const childrenInline = INLINE_PARENTS.has(copy.type);
        copy.content = copy.content.map((child) => rename(child, childrenInline, fromTheEditor));
        if (fromTheEditor && copy.content.length === 0) {
            delete copy.content;
        }
    }

    return copy;
}

function withoutNulls(attributes) {
    return Object.fromEntries(Object.entries(attributes).filter(([, value]) => value !== null && value !== undefined));
}

function withoutAttrs(mark) {
    const { attrs: _, ...rest } = mark;
    return rest;
}

// A document with nothing in it but one empty paragraph, which the editor holds for an empty field.
export function isEmpty(document) {
    const content = document?.content ?? [];
    return content.length === 0 || (content.length === 1 && content[0].type === "paragraph" && !(content[0].content?.length > 0));
}
