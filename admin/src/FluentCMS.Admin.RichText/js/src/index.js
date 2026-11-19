// The rich text editor of the admin (spec 4.7): Tiptap on the server's rich text model, driven by RichTextFieldEditor. The
// document goes to Blazor as JSON, debounced, and Blazor holds the form's value; the toolbar is Blazor's markup, whose
// roving focus this module keeps. No access token or request goes through here.
import { Editor, getSchema } from "@tiptap/core";
import { UndoRedo } from "@tiptap/extensions";
import { Slice } from "@tiptap/pm/model";
import { blockIds, extensions, fits, fromEditor, isEmpty, isSafeHref, randomId, toEditor, withFreshIds } from "./schema.js";

const DELAY = 250;

const COMMANDS = {
    bold: (chain) => chain.toggleBold(),
    italic: (chain) => chain.toggleItalic(),
    underline: (chain) => chain.toggleUnderline(),
    strike: (chain) => chain.toggleStrike(),
    code: (chain) => chain.toggleCode(),
    paragraph: (chain) => chain.setParagraph(),
    heading1: (chain) => chain.toggleHeading({ level: 1 }),
    heading2: (chain) => chain.toggleHeading({ level: 2 }),
    heading3: (chain) => chain.toggleHeading({ level: 3 }),
    bulletList: (chain) => chain.toggleBulletList(),
    orderedList: (chain) => chain.toggleOrderedList(),
    blockquote: (chain) => chain.toggleBlockquote(),
    codeBlock: (chain) => chain.toggleCodeBlock(),
    horizontalRule: (chain) => chain.setHorizontalRule(),
    undo: (chain) => chain.undo(),
    redo: (chain) => chain.redo(),
};

const STATES = [
    ["bold", (editor) => editor.isActive("bold")],
    ["italic", (editor) => editor.isActive("italic")],
    ["underline", (editor) => editor.isActive("underline")],
    ["strike", (editor) => editor.isActive("strike")],
    ["code", (editor) => editor.isActive("code")],
    ["paragraph", (editor) => editor.isActive("paragraph")],
    ["heading1", (editor) => editor.isActive("heading", { level: 1 })],
    ["heading2", (editor) => editor.isActive("heading", { level: 2 })],
    ["heading3", (editor) => editor.isActive("heading", { level: 3 })],
    ["bulletList", (editor) => editor.isActive("bulletList")],
    ["orderedList", (editor) => editor.isActive("orderedList")],
    ["blockquote", (editor) => editor.isActive("blockquote")],
    ["codeBlock", (editor) => editor.isActive("codeBlock")],
    ["link", (editor) => editor.isActive("link")],
];

let stylesheet;

// The editor's own styles, loaded beside the module so that they are not in the first download either.
function loadStyles() {
    stylesheet ??= new Promise((resolve) => {
        const link = document.createElement("link");
        link.rel = "stylesheet";
        link.href = new URL("./rich-text.css", import.meta.url).href;
        link.onload = link.onerror = () => resolve();
        document.head.appendChild(link);
    });
    return stylesheet;
}

// The document of a value, or undefined for one that is not JSON; null is an empty field.
function parse(json) {
    if (!json) {
        return null;
    }

    try {
        return JSON.parse(json);
    } catch {
        return undefined;
    }
}

function fitting(schema, json) {
    const document = parse(json);
    return document === null || (document !== undefined && fits(schema, document));
}

// The value Blazor holds: the document as the server's model, or null for an empty one, so that a required field is empty.
function valueOf(editor) {
    const document = fromEditor(editor.getJSON());
    return isEmpty(document) ? null : JSON.stringify(document);
}

function isMod(event) {
    return event.ctrlKey || event.metaKey;
}

// The toolbar's buttons take focus one at a time: the arrows move between them, Home and End to the ends, Escape back to the
// text, and Alt+F10 in the text to the toolbar.
function rovingFocus(toolbar, editor) {
    let current = 0;
    const buttons = () => [...toolbar.querySelectorAll("button")];
    const mark = () => buttons().forEach((button, index) => button.setAttribute("tabindex", index === current ? "0" : "-1"));
    const move = (index) => {
        const all = buttons();
        if (all.length === 0) {
            return;
        }

        current = (index + all.length) % all.length;
        mark();
        all[current].focus();
    };

    const onKeyDown = (event) => {
        const all = buttons();
        const index = all.indexOf(event.target);
        if (index < 0) {
            return;
        }

        const rtl = getComputedStyle(toolbar).direction === "rtl";
        const next = rtl ? "ArrowLeft" : "ArrowRight";
        const previous = rtl ? "ArrowRight" : "ArrowLeft";
        if (event.key === next) {
            move(index + 1);
        } else if (event.key === previous) {
            move(index - 1);
        } else if (event.key === "Home") {
            move(0);
        } else if (event.key === "End") {
            move(all.length - 1);
        } else if (event.key === "Escape") {
            editor.commands.focus();
        } else {
            return;
        }

        event.preventDefault();
    };

    const onFocusIn = (event) => {
        const index = buttons().indexOf(event.target);
        if (index >= 0 && index !== current) {
            current = index;
            mark();
        }
    };

    toolbar.addEventListener("keydown", onKeyDown);
    toolbar.addEventListener("focusin", onFocusIn);
    mark();

    return {
        focus: () => move(current),
        refresh: mark,
        dispose: () => {
            toolbar.removeEventListener("keydown", onKeyDown);
            toolbar.removeEventListener("focusin", onFocusIn);
        },
    };
}

// Whether the editor runs on a Mac, where Tiptap's Mod is Command rather than Control.
export function isMac() {
    return /Mac|iPhone|iPad/.test(navigator.userAgentData?.platform ?? navigator.platform ?? "");
}

// Creates the editor in `element`, with the toolbar `toolbar` (null when read-only), and calls back `host` with
// OnChangeAsync(json), OnStateAsync(states) and OnLinkRequestedAsync(). It answers null, and creates nothing, for a value the
// editor cannot hold whole, which the component shows read-only instead.
export async function create(element, toolbar, host, json, options) {
    const origin = randomId();
    const schema = getSchema(extensions(options.labels, origin));
    if (!fitting(schema, json)) {
        return null;
    }

    await loadStyles();

    let editor = null;
    let timer = null;
    // Whether a transaction changed the document since the value was last sent; nothing else is sent, so focus and blur on a
    // document the editor normalised, such as its order of marks, are no change.
    let pending = false;
    let state = "";

    // A call after Blazor let the component go has no one to answer it.
    const call = (method, ...args) => host.invokeMethodAsync(method, ...args).catch(() => {});

    const take = () => {
        clearTimeout(timer);
        timer = null;
        const changed = pending;
        pending = false;
        return { changed, value: changed ? valueOf(editor) : null };
    };

    const send = () => {
        const { changed, value } = take();
        if (changed) {
            call("OnChangeAsync", value);
        }
    };

    // The first change of a burst goes at once, so the form knows it has unsaved changes; the rest follow once typing pauses.
    const onUpdate = () => {
        const first = timer === null;
        pending = true;
        if (first) {
            send();
        } else {
            clearTimeout(timer);
        }

        timer = setTimeout(send, DELAY);
    };

    const report = (target) => {
        const active = STATES.filter(([, isActive]) => isActive(target)).map(([name]) => name);
        const next = active.join(" ");
        if (next !== state) {
            state = next;
            call("OnStateAsync", active);
        }
    };

    const attributes = {
        id: options.textboxId,
        role: "textbox",
        "aria-multiline": "true",
        "aria-labelledby": options.labelledBy,
        class: "rich-text-content",
        spellcheck: "true",
    };

    const initial = parse(json);
    editor = new Editor({
        element,
        injectCSS: false,
        enableContentCheck: true,
        editable: options.editable,
        content: initial ? toEditor(initial) : null,
        extensions: [...extensions(options.labels, origin), UndoRedo],
        editorProps: {
            attributes,
            // A copy of an inline block pasted beside its original takes a fresh id; one moved, by drag or cut, keeps its own.
            transformPasted: (slice, view) => {
                if (view.dragging?.move || slice.size === 0) {
                    return slice;
                }

                return Slice.fromJSON(view.state.schema, withFreshIds(slice.toJSON(), blockIds(view.state.doc)));
            },
        },
        onUpdate,
        onTransaction: ({ editor: target }) => report(target),
        onBlur: send,
    });

    let roving = toolbar ? rovingFocus(toolbar, editor) : null;
    let direction = options.dir;

    const setDirection = (lang, dir) => {
        direction = dir;
        editor.view.dom.setAttribute("lang", lang ?? "");
        editor.view.dom.setAttribute("dir", dir);
        toolbar?.setAttribute("dir", dir);
    };

    const describe = (describedBy, invalid) => {
        if (describedBy) {
            editor.view.dom.setAttribute("aria-describedby", describedBy);
        } else {
            editor.view.dom.removeAttribute("aria-describedby");
        }

        editor.view.dom.setAttribute("aria-invalid", String(invalid));
    };

    setDirection(options.lang, options.dir);
    describe(options.describedBy, options.invalid);
    editor.view.dom.setAttribute("aria-readonly", String(!options.editable));

    // The form's shortcuts save and publish from the document; the change goes to Blazor first.
    const onKeyDown = (event) => {
        if (isMod(event) && (event.key.toLowerCase() === "s" || event.key === "Enter")) {
            send();
        } else if (isMod(event) && !event.shiftKey && !event.altKey && event.key.toLowerCase() === "k") {
            event.preventDefault();
            call("OnLinkRequestedAsync");
        } else if (event.altKey && event.key === "F10" && roving) {
            event.preventDefault();
            roving.focus();
        }
    };
    editor.view.dom.addEventListener("keydown", onKeyDown);
    report(editor);

    return {
        // A value Blazor sets from outside the editor, such as the other side of a conflict, replaces the document; one the
        // editor cannot hold whole is refused, and the component shows it read-only.
        setContent: (value) => {
            if (!fitting(editor.schema, value)) {
                return false;
            }

            clearTimeout(timer);
            timer = null;
            pending = false;
            const next = parse(value);
            editor.commands.setContent(next ? toEditor(next) : null, { emitUpdate: false });
            return true;
        },
        setEditable: (editable) => {
            editor.setEditable(editable, false);
            editor.view.dom.setAttribute("aria-readonly", String(!editable));
        },
        describe,
        setDirection,
        // The toolbar Blazor drew again, or null once the field is read-only.
        setToolbar: (element) => {
            roving?.dispose();
            toolbar = element;
            roving = toolbar ? rovingFocus(toolbar, editor) : null;
            toolbar?.setAttribute("dir", direction);
        },
        run: (command) => {
            const apply = COMMANDS[command];
            return apply ? apply(editor.chain().focus()).run() : false;
        },
        focus: () => editor.commands.focus(),
        // The change not yet sent, if any, which the component gives the form before a save and as it goes.
        flush: take,
        // The href of the link at the selection, or null.
        link: () => (editor.isActive("link") ? editor.getAttributes("link").href ?? null : null),
        // A link the server would refuse is not set; an empty selection outside a link gets the address as its text.
        setLink: (href) => {
            const value = href?.trim();
            if (!isSafeHref(value)) {
                return false;
            }

            const chain = editor.chain().focus();
            if (editor.state.selection.empty && !editor.isActive("link")) {
                chain.insertContent({ type: "text", text: value, marks: [{ type: "link", attrs: { href: value } }] }).run();
            } else {
                chain.extendMarkRange("link").setLink({ href: value }).run();
            }

            return true;
        },
        unsetLink: () => editor.chain().focus().extendMarkRange("link").unsetLink().run(),
        dispose: () => {
            clearTimeout(timer);
            roving?.dispose();
            editor.view.dom.removeEventListener("keydown", onKeyDown);
            editor.destroy();
        },
    };
}
