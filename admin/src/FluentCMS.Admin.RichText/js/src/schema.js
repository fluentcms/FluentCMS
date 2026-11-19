// The editor's schema is the server's rich text model (backend spec 4.2, src/FluentCMS.Core/RichText): the same node and
// mark names and attributes. Links to entries, media and inline blocks are kept as they are, not edited, until A-25 adds
// their interface. The server places media and inline blocks at block level or inline, and a ProseMirror node is one or the
// other, so the inline ones take a name of their own in the editor, which toEditor and fromEditor translate.
import { Mark, Node } from "@tiptap/core";
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

const MEDIA = "media";
const INLINE_BLOCK = "inlineBlock";
const MEDIA_INLINE = "mediaInline";
const INLINE_BLOCK_INLINE = "inlineBlockInline";

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

function media(name, inline, labels, origin) {
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
    });
}

function inlineBlock(name, inline, labels, origin) {
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
    });
}

function entryLink(origin) {
    return Mark.create({
        name: "entryLink",
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
    });
}

// Every extension of the editor; `labels` names the kept nodes in the interface's language, and `origin` marks what this
// editor puts on the clipboard. The content of each node is the server's (NodeSpec.cs, test/content.json) but the document's,
// which holds at least one block so that there is somewhere to type; toEditor gives an empty document its paragraph.
export function extensions(labels, origin) {
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
        entryLink(origin),
        media(MEDIA, false, labels, origin),
        media(MEDIA_INLINE, true, labels, origin),
        inlineBlock(INLINE_BLOCK, false, labels, origin),
        inlineBlock(INLINE_BLOCK_INLINE, true, labels, origin),
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
// one, so a copy pasted beside its original is a new block, and a block moved by cut and paste keeps its id (ADR 0002).
export function withFreshIds(slice, taken, random = randomId) {
    const seen = new Set(taken);
    const visit = (node) => {
        if (node === null || typeof node !== "object") {
            return node;
        }

        const copy = { ...node };
        if ((copy.type === INLINE_BLOCK || copy.type === INLINE_BLOCK_INLINE) && copy.attrs?.id) {
            let id = copy.attrs.id;
            while (seen.has(id)) {
                id = random();
            }

            seen.add(id);
            copy.attrs = { ...copy.attrs, id };
        }

        if (Array.isArray(copy.content)) {
            copy.content = copy.content.map(visit);
        }

        return copy;
    };

    return { ...slice, content: (slice.content ?? []).map(visit) };
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
