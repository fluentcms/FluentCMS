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

function media(name, inline, labels) {
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
                getAttrs: (element) => (GUID.test(element.getAttribute("data-media-id") ?? "") ? null : false),
            }];
        },
        renderHTML({ HTMLAttributes, node }) {
            return [inline ? "span" : "div", { ...HTMLAttributes, "data-fluentcms-media": "", class: "rich-text-kept", contenteditable: "false" },
                keptLabel(labels, MEDIA, node.attrs.alt ?? node.attrs.title)];
        },
        renderText: () => "",
    });
}

function inlineBlock(name, inline, labels) {
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
                // A pasted copy of a block takes no id, so the server gives it a new one (ADR 0002).
                id: { default: null, parseHTML: () => null, renderHTML: () => ({}) },
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
                getAttrs: (element) => (/^[A-Za-z][A-Za-z0-9_-]*$/.test(element.getAttribute("data-block-type") ?? "") ? null : false),
            }];
        },
        renderHTML({ HTMLAttributes, node }) {
            return [inline ? "span" : "div", { ...HTMLAttributes, "data-fluentcms-block": "", class: "rich-text-kept", contenteditable: "false" },
                keptLabel(labels, INLINE_BLOCK, node.attrs.blockType)];
        },
        renderText: () => "",
    });
}

// A link to an entry is kept with its two attributes; it is not made from pasted HTML, since an id there cannot be checked.
const EntryLink = Mark.create({
    name: "entryLink",
    inclusive: false,
    addAttributes() {
        return {
            entryId: { default: null, renderHTML: (attributes) => ({ "data-entry-id": attributes.entryId }) },
            entryType: { default: null, renderHTML: (attributes) => ({ "data-entry-type": attributes.entryType }) },
        };
    },
    parseHTML() {
        return [];
    },
    renderHTML({ HTMLAttributes }) {
        return ["span", { ...HTMLAttributes, class: "rich-text-entry-link" }, 0];
    },
});

// Every extension of the editor; `labels` names the kept nodes in the interface's language.
export function extensions(labels) {
    return [
        Document,
        Paragraph,
        Text,
        Heading.configure({ levels: [1, 2, 3, 4, 5, 6] }),
        Blockquote,
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
        BulletList,
        // A list without a start has none in the server's model, so the editor adds none either.
        OrderedList.extend({
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
        ListItem,
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
        EntryLink,
        media(MEDIA, false, labels),
        media(MEDIA_INLINE, true, labels),
        inlineBlock(INLINE_BLOCK, false, labels),
        inlineBlock(INLINE_BLOCK_INLINE, true, labels),
    ];
}

// A document of the server's model as the editor's JSON.
export function toEditor(document) {
    return rename(document, false);
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
